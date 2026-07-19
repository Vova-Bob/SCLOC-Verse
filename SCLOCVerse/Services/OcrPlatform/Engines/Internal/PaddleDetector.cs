using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;

namespace SCLOCVerse.Services.OcrPlatform.Engines.Internal
{
    /// <summary>
    /// Text Detection model (DB — Differentiable Binarization) для PaddleOCR PP-OCRv5/v6.
    /// Повертає список текстових регіонів (TextBoxes) у координатах оригінального зображення.
    ///
    /// Pipeline:
    /// 1. Resize до target size (maxSideLen, зберігаючи пропорції)
    /// 2. Normalize (ImageNet mean/std → NCHW tensor)
    /// 3. InferenceSession.Run → probability map [1, 1, H, W]
    /// 4. Threshold → Dilate → FindContours
    /// 5. Per contour: GetMiniBox + Score + Unclip
    /// 6. Scale back to original coordinates
    ///
    /// Skip AngleNet — SC HUD текст завжди вертикальний.
    /// </summary>
    internal sealed class PaddleDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;

        public PaddleDetector(string modelPath)
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED
            };
            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.First();
        }

        /// <summary>
        /// Знайти всі текстові регіони у зображенні.
        /// </summary>
        public List<DetectedTextBox> Detect(
            Mat srcBgr,
            int maxSideLen,
            float boxScoreThresh,
            float boxThresh,
            float unclipRatio)
        {
            // Крок 1: Resize до target (зберігаючи пропорції).
            var (resized, scale) = ResizeToTarget(srcBgr, maxSideLen);

            try
            {
                // Крок 2: Normalize → NCHW tensor.
                var tensorData = OcrPreprocessing.NormalizeForDetection(resized);
                var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, resized.Rows, resized.Cols });

                // Крок 3: Run inference.
                var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
                using var results = _session.Run(inputs);
                var output = results.First().AsTensor<float>();

                // Крок 4-6: Post-process probability map → TextBoxes.
                return PostProcess(
                    output,
                    resized.Rows,
                    resized.Cols,
                    scale,
                    srcBgr.Cols,
                    srcBgr.Rows,
                    boxScoreThresh,
                    boxThresh,
                    unclipRatio);
            }
            finally
            {
                resized.Dispose();
            }
        }

        /// <summary>
        /// Resize зображення так, що найдовша сторона ≤ maxSideLen (зберігаючи пропорції).
        /// Повертає resized Mat + scale factor (src/dst).
        /// </summary>
        private static (Mat Resized, float Scale) ResizeToTarget(Mat src, int maxSideLen)
        {
            var srcMaxSide = Math.Max(src.Cols, src.Rows);
            float scale;
            int targetH, targetW;

            if (maxSideLen <= 0 || maxSideLen >= srcMaxSide)
            {
                scale = 1.0f;
                targetH = src.Rows;
                targetW = src.Cols;
            }
            else
            {
                scale = (float)srcMaxSide / maxSideLen;
                var ratio = (float)maxSideLen / srcMaxSide;
                targetH = (int)(src.Rows * ratio);
                targetW = (int)(src.Cols * ratio);
            }

            // PP-OCRv5_mobile_det очікує розміри кратні 32.
            targetH = RoundUpToMultiple(targetH, 32);
            targetW = RoundUpToMultiple(targetW, 32);

            var resized = new Mat();
            Cv2.Resize(src, resized, new Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
            return (resized, scale);
        }

        private static int RoundUpToMultiple(int value, int multiple)
        {
            return ((value + multiple - 1) / multiple) * multiple;
        }

        /// <summary>
        /// Постпроцесинг probability map → список DetectedTextBox.
        /// </summary>
        private static List<DetectedTextBox> PostProcess(
            Tensor<float> probabilityMap,
            int rows,
            int cols,
            float scale,
            int srcWidth,
            int srcHeight,
            float boxScoreThresh,
            float boxThresh,
            float unclipRatio)
        {
            // Створюємо масив pred [rows, cols] з probability map.
            var pred = new float[rows * cols];
            for (var i = 0; i < pred.Length; i++)
            {
                pred[i] = probabilityMap.GetValue(i);
            }

            // Створюємо бінарну маску: pixel = 255 якщо pred ≥ boxThresh.
            var cbufMat = new Mat(rows, cols, MatType.CV_8UC1);
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    var p = pred[y * cols + x];
                    cbufMat.Set(y, x, (byte)(p * 255 > boxThresh * 255 ? 255 : 0));
                }
            }

            // Dilate для з'єднання близьких компонентів.
            var dilateMat = new Mat();
            var element = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            Cv2.Dilate(cbufMat, dilateMat, element);

            // FindContours.
            Cv2.FindContours(dilateMat, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            var boxes = new List<DetectedTextBox>();
            foreach (var contour in contours)
            {
                if (contour.Length <= 2) continue;

                var minBox = GetMiniBox(contour, out var minEdge);
                if (minEdge < 3f) continue;

                var score = GetScore(contour, pred, cols);
                if (score < boxScoreThresh) continue;

                // Unclip — розширення bounding box на unclipRatio.
                var expandedBox = UnclipBox(minBox, unclipRatio);
                if (expandedBox == null) continue;

                // Перерахуємо minBox для expanded.
                var expandedMinBox = GetMiniBox(expandedBox, out var expandedEdge);
                if (expandedEdge < 5f) continue;

                // Scale back to original coordinates.
                var srcPoints = expandedMinBox
                    .Select(p => new Point(
                        Clamp((int)(p.X * scale), 0, srcWidth - 1),
                        Clamp((int)(p.Y * scale), 0, srcHeight - 1)))
                    .ToArray();

                boxes.Add(new DetectedTextBox
                {
                    Points = srcPoints,
                    Score = score
                });
            }

            // Reverse для natural reading order (top → bottom).
            boxes.Reverse();
            return boxes;
        }

        /// <summary>
        /// Мінімальний описаний прямокутник (rotated) через MinAreaRect.
        /// Повертає 4 точки у порядку: top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        private static Point[] GetMiniBox(Point[] contour, out float minEdge)
        {
            var rotatedRect = Cv2.MinAreaRect(contour);
            var points = rotatedRect.Points().Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
            minEdge = Math.Min(rotatedRect.Size.Width, rotatedRect.Size.Height);

            // Сортуємо точки за X, потім за Y — стандартний алгоритм з RapidOCR.
            var sorted = points.OrderBy(p => p.X).ToArray();
            var index1 = sorted[0].Y > sorted[1].Y ? 1 : 0;
            var index4 = sorted[0].Y > sorted[1].Y ? 0 : 1;
            var index2 = sorted[2].Y > sorted[3].Y ? 3 : 2;
            var index3 = sorted[2].Y > sorted[3].Y ? 2 : 3;

            return new[] { sorted[index1], sorted[index2], sorted[index3], sorted[index4] };
        }

        private static Point[] GetMiniBox(List<Point2f> contour, out float minEdge)
        {
            return GetMiniBox(contour.Select(p => new Point((int)p.X, (int)p.Y)).ToArray(), out minEdge);
        }

        /// <summary>
        /// Середнє значення probability map всередині контуру.
        /// </summary>
        private static float GetScore(Point[] contour, float[] pred, int cols)
        {
            var xmin = (int)contour.Min(p => p.X);
            var xmax = (int)contour.Max(p => p.X);
            var ymin = (int)contour.Min(p => p.Y);
            var ymax = (int)contour.Max(p => p.Y);

            var width = xmax - xmin + 1;
            var height = ymax - ymin + 1;

            // Створюємо маску з контуру (заповнений полігон).
            using var mask = Mat.Zeros(height, width, MatType.CV_8UC1).ToMat();
            var shiftedContour = contour.Select(p => new Point(p.X - xmin, p.Y - ymin)).ToArray();
            Cv2.FillPoly(mask, new[] { shiftedContour }, new Scalar(1));

            // Середнє pred всередині маски.
            double sum = 0;
            long count = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (mask.At<byte>(y, x) == 1)
                    {
                        sum += pred[(ymin + y) * cols + (xmin + x)];
                        count++;
                    }
                }
            }

                mask.Dispose();
                return count > 0 ? (float)(sum / count) : 0f;
        }

        /// <summary>
        /// Розширення bounding box (unclip) через просту геометрію.
        /// Замість ClipperOffset — рівномірне розширення від центроїда.
        /// Працює для горизонтального тексту (SC HUD) — не потребує складної геометрії.
        /// </summary>
        private static List<Point2f>? UnclipBox(Point[] box, float ratio)
        {
            if (box.Length != 4) return null;

            // Центроїд.
            var cx = box.Average(p => p.X);
            var cy = box.Average(p => p.Y);

            // Площа + периметр.
            var area = Math.Abs(PolygonArea(box));
            var perimeter = PolygonPerimeter(box);
            if (perimeter < 1) return null;

            // Distance = area × ratio / perimeter (формула PaddleOCR).
            var distance = area * ratio / perimeter;

            // Розширити кожну точку від центроїда на distance.
            var result = new List<Point2f>(4);
            foreach (var p in box)
            {
                var dx = p.X - cx;
                var dy = p.Y - cy;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001)
                {
                    result.Add(new Point2f(p.X, p.Y));
                    continue;
                }

                var nx = dx / len;
                var ny = dy / len;
                result.Add(new Point2f((float)(p.X + nx * distance), (float)(p.Y + ny * distance)));
            }

            return result;
        }

        private static double PolygonArea(Point[] pts)
        {
            double area = 0;
            for (var i = 0; i < pts.Length; i++)
            {
                var j = (i + 1) % pts.Length;
                area += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
            }
            return area / 2;
        }

        private static double PolygonPerimeter(Point[] pts)
        {
            double perimeter = 0;
            for (var i = 0; i < pts.Length; i++)
            {
                var j = (i + 1) % pts.Length;
                var dx = pts[j].X - pts[i].X;
                var dy = pts[j].Y - pts[i].Y;
                perimeter += Math.Sqrt(dx * dx + dy * dy);
            }
            return perimeter;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }

    /// <summary>
    /// Розпізнаний текстовий регіон (4 точки + score).
    /// </summary>
    internal sealed class DetectedTextBox
    {
        public Point[] Points { get; init; } = new Point[4];
        public float Score { get; init; }

        /// <summary>Прямокутник, що описує текстовий регіон (bounding box).</summary>
        public Rect BoundingRect
        {
            get
            {
                var minX = Points.Min(p => p.X);
                var minY = Points.Min(p => p.Y);
                var maxX = Points.Max(p => p.X);
                var maxY = Points.Max(p => p.Y);
                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
        }
    }
}
