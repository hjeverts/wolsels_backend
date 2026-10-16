namespace Wolsels.Api.Services;

/// <summary>Reads the dimensions from the header of a JPEG file, without decoding the whole image.</summary>
public static class JpegInfo
{
    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return false;

        var i = 2;
        while (i + 4 <= data.Length)
        {
            if (data[i] != 0xFF) return false;
            var marker = data[i + 1];
            // Skip padding (FF FF ...) and markers without a length.
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
            if (marker is 0xD9 or 0xDA) return false; // end, or image data before the SOF

            var length = (data[i + 2] << 8) | data[i + 3];
            if (length < 2 || i + 2 + length > data.Length) return false;
            // SOF0–SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7) return false;
                height = (data[i + 5] << 8) | data[i + 6];
                width = (data[i + 7] << 8) | data[i + 8];
                return width > 0 && height > 0;
            }
            i += 2 + length;
        }
        return false;
    }
}
