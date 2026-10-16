using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Copyless
{
    class OcrOutcome { public string Text = ""; public string Language = ""; }

    /// <summary>Text recognition using Windows.Media.Ocr.</summary>
    static class Ocr
    {
        static readonly Dictionary<string, OcrEngine> engines = new Dictionary<string, OcrEngine>();

        public static string[] Languages()
        {
            try { return OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray(); }
            catch { return new string[0]; }
        }

        static OcrEngine Engine(string tag)
        {
            OcrEngine e;
            if (engines.TryGetValue(tag, out e)) return e;
            try { e = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag)); } catch { e = null; }
            engines[tag] = e;
            return e;
        }

        public static async Task<OcrOutcome> Recognize(Bitmap source)
        {
            var best = new OcrOutcome();
            using (Bitmap scaled = Prepare(source, false))
            {
                best = await RecognizeBest(scaled);
                if (Letters(best.Text) >= 2) return best;
            }
            // Retry with inverted colors for light-on-dark text.
            using (Bitmap inverted = Prepare(source, true))
            {
                OcrOutcome inv = await RecognizeBest(inverted);
                if (Letters(inv.Text) > Letters(best.Text)) best = inv;
            }
            return best;
        }

        static async Task<OcrOutcome> RecognizeBest(Bitmap bmp)
        {
            SoftwareBitmap sb = await ToSoftwareBitmap(bmp);
            string pref = Settings.OcrLanguage;
            List<string> tags;
            if (!string.IsNullOrEmpty(pref) && !pref.Equals("auto", StringComparison.OrdinalIgnoreCase) && Engine(pref) != null)
                tags = new List<string> { pref };
            else
                tags = Languages().ToList();

            var results = new List<OcrOutcome>();
            foreach (string tag in tags)
            {
                OcrEngine e = Engine(tag);
                if (e == null) continue;
                OcrResult r = await Await(e.RecognizeAsync(sb));
                var text = string.Join("\r\n", r.Lines.Select(l => l.Text));
                results.Add(new OcrOutcome { Text = text.Trim(), Language = tag });
            }
            if (results.Count == 0) return new OcrOutcome();
            if (results.Count == 1) return results[0];

            // Prefer a non-Latin engine only when its output is mostly non-Latin.
            OcrOutcome pick = null;
            foreach (var r in results)
                if (NonLatinShare(r.Text) > 0.6 && (pick == null || Letters(r.Text) > Letters(pick.Text))) pick = r;
            if (pick != null) return pick;
            // Otherwise pick the result with the fewest non-Latin characters.
            return results.OrderBy(r => Letters(r.Text) == 0 ? 2.0 : NonLatinShare(r.Text))
                          .ThenByDescending(r => Letters(r.Text)).First();
        }

        static int Letters(string s) { return s.Count(char.IsLetterOrDigit); }

        static double NonLatinShare(string s)
        {
            int letters = 0, nonLatin = 0;
            foreach (char c in s)
            {
                if (!char.IsLetter(c)) continue;
                letters++;
                if (c > 0x024F) nonLatin++;
            }
            return letters == 0 ? 0 : (double)nonLatin / letters;
        }

        /// <summary>Scales the image up so small text is recognized reliably.</summary>
        static Bitmap Prepare(Bitmap src, bool invert)
        {
            int max = (int)Math.Min(OcrEngine.MaxImageDimension, 8000u);
            float scale = src.Height < 60 ? 4f : src.Height < 200 ? 3f : src.Height < 600 ? 2f : 1f;
            scale = Math.Min(scale, (float)max / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    if (invert)
                        ia.SetColorMatrix(new ColorMatrix(new[] {
                            new float[] { -1, 0, 0, 0, 0 }, new float[] { 0, -1, 0, 0, 0 }, new float[] { 0, 0, -1, 0, 0 },
                            new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 } }));
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return dst;
        }

        static async Task<SoftwareBitmap> ToSoftwareBitmap(Bitmap bmp)
        {
            byte[] bytes;
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Bmp); bytes = ms.ToArray(); }
            var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream.GetOutputStreamAt(0));
            writer.WriteBytes(bytes);
            await Await(writer.StoreAsync());
            writer.DetachStream();
            stream.Seek(0);
            BitmapDecoder decoder = await Await(BitmapDecoder.CreateAsync(stream));
            return await Await(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
        }

        /// <summary>Converts a WinRT async operation to a Task.</summary>
        static Task<T> Await<T>(IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (o, status) =>
            {
                try
                {
                    if (status == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                    else if (status == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode ?? new Exception("WinRT operation failed"));
                    else tcs.TrySetCanceled();
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
            return tcs.Task;
        }

        public static Bitmap CaptureScreen(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        /// <summary>True if the capture is a single solid color (e.g. exclusive fullscreen).</summary>
        public static bool LooksBlank(Bitmap b)
        {
            Color first = b.GetPixel(0, 0);
            int stepX = Math.Max(1, b.Width / 24), stepY = Math.Max(1, b.Height / 24);
            for (int y = 0; y < b.Height; y += stepY)
                for (int x = 0; x < b.Width; x += stepX)
                    if (b.GetPixel(x, y).ToArgb() != first.ToArgb()) return false;
            return true;
        }
    }
}
