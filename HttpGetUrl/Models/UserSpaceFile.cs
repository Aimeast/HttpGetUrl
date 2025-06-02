using System.Buffers.Text;

namespace HttpGetUrl.Models;

public class UserSpaceFile
{
    public static int UserSpaceLength => 6;

    public string Space { get; set; }
    public DateTimeOffset Expires { get; set; }

    public void Decode(string value)
    {
        try
        {
            var dot = value.IndexOf('.');
            if (dot == -1)
            {
                dot = value.Length;
                value += ".0";
            }
            var base64 = value[..dot];
            var time = value[(dot + 1)..];

            if (Base64Url.IsValid(base64, out var decodedLength) && decodedLength == UserSpaceLength)
            {
                Expires = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(time));
                Space = base64;
            }
        }
        catch
        {
            Space = null;
            Expires = DateTimeOffset.MinValue;
        }
    }

    public string Encode()
    {
        return $"{Space}.{Expires.ToUnixTimeMilliseconds()}";
    }
}
