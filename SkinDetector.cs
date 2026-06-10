using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using AForge.Imaging;          // BlobCounter, Blob
using AForge.Imaging.Filters;  // Erosion, Dilatation

namespace ProyectoFinalPDI.AForge
{
    public class SkinDetector
    {
        public class BodyPoints
        {
            public PointF Head { get; set; } = new PointF(-1, -1);
            public PointF ShoulderL { get; set; } = new PointF(-1, -1);
            public PointF ShoulderR { get; set; } = new PointF(-1, -1);
            public PointF HandL { get; set; } = new PointF(-1, -1);
            public PointF HandR { get; set; } = new PointF(-1, -1);
            public PointF HipCenter { get; set; } = new PointF(-1, -1);
            public bool IsValid { get; set; }
        }

        private readonly BlobCounter _blobCounter = new BlobCounter
        {
            FilterBlobs = true,
            MinWidth = 30,
            MinHeight = 30,
            ObjectsOrder = ObjectsOrder.Size
        };

        // Suavizado temporal de puntos
        private PointF _lastHead = new PointF(-1, -1);
        private PointF _lastShoulderL = new PointF(-1, -1);
        private PointF _lastShoulderR = new PointF(-1, -1);
        private PointF _lastHandL = new PointF(-1, -1);
        private PointF _lastHandR = new PointF(-1, -1);
        private const float SMOOTHING = 0.6f; // 0 = sigue exactamente, 1 = no se mueve

        public BodyPoints Detect(Bitmap frame)
        {
            var result = new BodyPoints();
            int W = frame.Width, H = frame.Height;

            Bitmap skinMask = ApplySkinMask(frame);

            _blobCounter.ProcessImage(skinMask);
            Blob[] blobs = _blobCounter.GetObjectsInformation();

            if (blobs.Length == 0)
            {
                skinMask.Dispose();
                return result;
            }

            Array.Sort(blobs, (a, b) => a.Rectangle.Top.CompareTo(b.Rectangle.Top));

            Blob headBlob = null;
            foreach (var b in blobs)
            {
                float area = b.Rectangle.Width * (float)b.Rectangle.Height;
                if (area > 400 && area < W * H * 0.15f)
                {
                    headBlob = b;
                    break;
                }
            }

            if (headBlob == null) { skinMask.Dispose(); return result; }

            result.Head = Normalize(
                new PointF(headBlob.Rectangle.X + headBlob.Rectangle.Width / 2f,
                           headBlob.Rectangle.Y + headBlob.Rectangle.Height * 0.3f), W, H);

            float headCX = headBlob.Rectangle.X + headBlob.Rectangle.Width / 2f;
            float shoulderY = headBlob.Rectangle.Bottom + headBlob.Rectangle.Height * 0.5f;
            float shoulderSpread = W * 0.18f;

            result.ShoulderL = Normalize(new PointF(headCX - shoulderSpread, shoulderY), W, H);
            result.ShoulderR = Normalize(new PointF(headCX + shoulderSpread, shoulderY), W, H);

            float hipY = shoulderY + headBlob.Rectangle.Height * 2.2f;
            result.HipCenter = Normalize(new PointF(headCX, hipY), W, H);

            var handCandidates = new List<Blob>();
            foreach (var b in blobs)
            {
                if (b == headBlob) continue;
                float area = b.Rectangle.Width * (float)b.Rectangle.Height;
                if (area < 200 || area > W * H * 0.10f) continue;
                float bCX = b.Rectangle.X + b.Rectangle.Width / 2f;
                if (Math.Abs(bCX - headCX) > W * 0.08f)
                    handCandidates.Add(b);
            }

            handCandidates.Sort((a, b) =>
            {
                float aCX = a.Rectangle.X + a.Rectangle.Width / 2f;
                float bCX2 = b.Rectangle.X + b.Rectangle.Width / 2f;
                return aCX.CompareTo(bCX2);
            });

            if (handCandidates.Count >= 1)
            {
                var hL = handCandidates[0];
                result.HandL = Normalize(
                    new PointF(hL.Rectangle.X + hL.Rectangle.Width / 2f,
                               hL.Rectangle.Y + hL.Rectangle.Height / 2f), W, H);
            }
            else
                result.HandL = Normalize(new PointF(headCX - shoulderSpread * 2f, shoulderY), W, H);

            if (handCandidates.Count >= 2)
            {
                var hR = handCandidates[handCandidates.Count - 1];
                result.HandR = Normalize(
                    new PointF(hR.Rectangle.X + hR.Rectangle.Width / 2f,
                               hR.Rectangle.Y + hR.Rectangle.Height / 2f), W, H);
            }
            else
                result.HandR = Normalize(new PointF(headCX + shoulderSpread * 2f, shoulderY), W, H);

            result.IsValid = true;
            
            // Aplicar suavizado temporal
            result.Head = SmoothPoint(result.Head, _lastHead);
            result.ShoulderL = SmoothPoint(result.ShoulderL, _lastShoulderL);
            result.ShoulderR = SmoothPoint(result.ShoulderR, _lastShoulderR);
            result.HandL = SmoothPoint(result.HandL, _lastHandL);
            result.HandR = SmoothPoint(result.HandR, _lastHandR);
            
            // Guardar para el siguiente frame
            _lastHead = result.Head;
            _lastShoulderL = result.ShoulderL;
            _lastShoulderR = result.ShoulderR;
            _lastHandL = result.HandL;
            _lastHandR = result.HandR;
            
            skinMask.Dispose();
            return result;
        }
        
        private static PointF SmoothPoint(PointF current, PointF previous)
        {
            if (previous.X < 0 || previous.Y < 0) return current; // Primer frame
            
            float dist = (float)Math.Sqrt((current.X - previous.X) * (current.X - previous.X) + 
                                         (current.Y - previous.Y) * (current.Y - previous.Y));
            
            // Si el salto es muy grande, es probablemente ruido - usar anterior
            if (dist > 0.2f)
                return previous;
            
            // Suavizar interpolando
            return new PointF(
                previous.X * SMOOTHING + current.X * (1 - SMOOTHING),
                previous.Y * SMOOTHING + current.Y * (1 - SMOOTHING));
        }

        private static PointF Normalize(PointF p, int w, int h)
            => new PointF(
                Math.Max(0f, Math.Min(1f, p.X / w)),
                Math.Max(0f, Math.Min(1f, p.Y / h)));

        private static Bitmap ApplySkinMask(Bitmap src)
        {
            Bitmap bmp = src.Clone(
                new Rectangle(0, 0, src.Width, src.Height),
                PixelFormat.Format24bppRgb);

            Bitmap result = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format8bppIndexed);

            ColorPalette palette = result.Palette;
            for (int i = 0; i < 256; i++)
                palette.Entries[i] = Color.FromArgb(i, i, i);
            result.Palette = palette;

            BitmapData srcData = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            BitmapData dstData = result.LockBits(
                new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

            unsafe
            {
                byte* s = (byte*)srcData.Scan0;
                byte* d = (byte*)dstData.Scan0;
                int sw = srcData.Stride;
                int dw = dstData.Stride;

                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        byte B = s[y * sw + x * 3];
                        byte G = s[y * sw + x * 3 + 1];
                        byte R = s[y * sw + x * 3 + 2];

                        double Y2 = 0.299 * R + 0.587 * G + 0.114 * B;
                        double Cb = -0.169 * R - 0.331 * G + 0.500 * B + 128;
                        double Cr = 0.500 * R - 0.419 * G - 0.081 * B + 128;

                        // Umbrales más amplios para detectar piel en diferentes iluminaciones
                        bool isSkin = Y2 > 50 &&
                                      Cb >= 77 && Cb <= 145 &&
                                      Cr >= 133 && Cr <= 190;

                        d[y * dw + x] = isSkin ? (byte)255 : (byte)0;
                    }
                }
            }

            bmp.UnlockBits(srcData);
            result.UnlockBits(dstData);
            bmp.Dispose();

            // Filtros morfológicos mejorados para reducir ruido
            Erosion erode = new Erosion();
            Dilatation dilate = new Dilatation();
            
            // Erosión para eliminar ruido pequeño
            erode.ApplyInPlace(result);
            erode.ApplyInPlace(result);
            
            // Dilatación para rellenar huecos
            dilate.ApplyInPlace(result);
            dilate.ApplyInPlace(result);
            dilate.ApplyInPlace(result);
            dilate.ApplyInPlace(result);

            return result;
        }
    }
}