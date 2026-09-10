using System.Drawing;
using System.Windows.Media.Imaging;

namespace SnipTranslate.Capture;

internal sealed record CaptureFrame(Rectangle Bounds, BitmapSource Bitmap);

