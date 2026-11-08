using System.IO;
using System.Windows.Media.Imaging;

namespace ClickBoard.Infrastructure.Windows;

public static class ImageThumbnail
{
    public static byte[]? Create(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
            bitmap.StreamSource = input;
            bitmap.EndInit();
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
    }
}
