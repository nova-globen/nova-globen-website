using System;

namespace Didin.Server.Services
{
    /// <summary>
    /// An image's width and height read from its header (JPEG or PNG), without decoding it: whether a poster is the
    /// usual portrait or wide art (a film still, key art), which the cards show whole instead of cropping.
    /// </summary>
    public static class ImageSize
    {
        /// <summary>(width, height), or null when the bytes are not a JPEG or PNG this can read.</summary>
        public static (int Width, int Height)? Read(byte[] image)
        {
            if (image == null || image.Length < 24)
            {
                return null;
            }
            // PNG: the signature, then IHDR's width and height, big-endian
            if (image[0] == 0x89 && image[1] == 0x50 && image[2] == 0x4E && image[3] == 0x47)
            {
                return (BigEndian32(image, 16), BigEndian32(image, 20));
            }
            if (image[0] != 0xFF || image[1] != 0xD8)
            {
                return null;
            }
            // JPEG: walk the segments to the first start-of-frame (baseline, extended or progressive)
            var i = 2;
            while (i + 9 < image.Length)
            {
                if (image[i] != 0xFF)
                {
                    return null;
                }
                var marker = image[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }
                var length = (image[i + 2] << 8) | image[i + 3];
                if (marker is 0xC0 or 0xC1 or 0xC2)
                {
                    var height = (image[i + 5] << 8) | image[i + 6];
                    var width = (image[i + 7] << 8) | image[i + 8];
                    return width > 0 && height > 0 ? (width, height) : null;
                }
                if (length < 2)
                {
                    return null;
                }
                i += 2 + length;
            }
            return null;
        }

        /// <summary>True for art wider than it is tall.</summary>
        public static bool IsWide(int? width, int? height) => width > 0 && height > 0 && width > height;

        private static int BigEndian32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
    }
}
