using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace AlKhayal.Printing
{
    // Embeds the native Arabic renderer's image in a single PDF page. No fonts or
    // external PDF runtime are required on a standalone terminal.
    internal static class InvoicePdf
    {
        public static byte[] FromPng(byte[] png)
        {
            using (var input = new MemoryStream(png))
            using (var source = Image.FromStream(input))
            using (var bitmap = new Bitmap(source.Width, source.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var jpeg = new MemoryStream())
            using (var output = new MemoryStream())
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                bitmap.Save(jpeg, ImageFormat.Jpeg);
                var width = source.Width * 72.0 / Math.Max(1, source.HorizontalResolution);
                var height = source.Height * 72.0 / Math.Max(1, source.VerticalResolution);
                var offsets = new long[6];
                Write(output, "%PDF-1.4\n");
                Object(output, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
                Object(output, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
                Object(output, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(width) + " " + N(height) + "] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
                offsets[4] = output.Position;
                Write(output, "4 0 obj\n<< /Type /XObject /Subtype /Image /Width " + source.Width + " /Height " + source.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpeg.Length + " >>\nstream\n");
                jpeg.Position = 0; jpeg.CopyTo(output);
                Write(output, "\nendstream\nendobj\n");
                var content = "q\n" + N(width) + " 0 0 " + N(height) + " 0 0 cm\n/Im0 Do\nQ\n";
                offsets[5] = output.Position;
                Write(output, "5 0 obj\n<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream\nendobj\n");
                var start = output.Position;
                Write(output, "xref\n0 6\n0000000000 65535 f \n");
                for (var i = 1; i <= 5; i++) Write(output, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write(output, "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + start.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
                return output.ToArray();
            }
        }
        private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static void Write(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
        private static void Object(Stream stream, long[] offsets, int id, string body)
        {
            offsets[id] = stream.Position;
            Write(stream, id + " 0 obj\n" + body + "\nendobj\n");
        }
    }
}
