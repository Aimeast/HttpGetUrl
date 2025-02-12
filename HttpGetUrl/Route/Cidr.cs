using System.Net;
using System.Net.Sockets;

namespace HttpGetUrl.Route;

public sealed class Cidr : IComparable<Cidr>
{
    private byte[] bytes = null;

    public IPAddress IPAddress { get; private set; }

    public int Subnet { get; private set; }

    public bool IsIPv6
    {
        get { return IPAddress.AddressFamily == AddressFamily.InterNetworkV6; }
    }

    #region CIDR Algorithm
    public bool IsPaired(Cidr cidr)
    {
        if (cidr == null)
            throw new ArgumentNullException(nameof(cidr));
        if (Subnet == 0 || cidr.Subnet == 0)
            throw new NotSupportedException("Subnet=0");

        if (IsIPv6 != cidr.IsIPv6 || Subnet != cidr.Subnet)
            return false;

        int k = Subnet - 1;
        int i = k >> 3;
        int j = 7 - k & 7;

        if ((bytes[i] ^ cidr.bytes[i]) != 1 << j)
            return false;

        while (i-- > 0)
        {
            if (bytes[i] != cidr.bytes[i])
                return false;
        }

        return true;
    }

    public Cidr GetBiggerSubnet()
    {
        if (Subnet == 0)
            throw new NotSupportedException("Subnet=0");
        var cidr = new Cidr();
        cidr.Subnet = Subnet - 1;
        cidr.bytes = new byte[bytes.Length];
        bytes.CopyTo(cidr.bytes, 0);

        int k = Subnet - 1;
        int i = k >> 3;
        int j = 7 - k & 7;
        cidr.bytes[i] = (byte)(cidr.bytes[i] & ~(1 << j));
        cidr.IPAddress = new IPAddress(cidr.bytes);

        return cidr;
    }

    public bool ExistsIntersection(Cidr other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        if (IsIPv6 != other.IsIPv6)
            return false;

        if (Subnet == 0 || other.Subnet == 0)
            return true;

        int min = Math.Min(Subnet, other.Subnet) - 1;
        int length = min >> 3;

        int k;
        for (k = 0; k < length; k++)
            if (bytes[k] != other.bytes[k])
                return false;

        //int m = (0xff << (7 - min & 7)) & 0xff;
        int m = 0xff80 >> (min & 7);
        if (k < bytes.Length && (bytes[k] & m) != (other.bytes[k] & m))
            return false;

        return true;
    }
    #endregion

    public override bool Equals(object obj)
    {
        Cidr cidr = obj as Cidr;
        if (cidr == null)
            return false;
        return Subnet == cidr.Subnet && IPAddress.Equals(cidr.IPAddress);
    }

    public override int GetHashCode()
    {
        return IPAddress.GetHashCode() ^ Subnet.GetHashCode();
    }

    public override string ToString()
    {
        return string.Format("{0}/{1}", IPAddress, Subnet);
    }

    public static Cidr Parse(string str)
    {
        Cidr cidr = new Cidr();
        cidr.SetFromString(str);
        return cidr;
    }

    private void SetFromString(string str)
    {
        int i = str.IndexOf('/');
        IPAddress = IPAddress.Parse(str.Substring(0, i));
        Subnet = int.Parse(str.Substring(i + 1));
        bytes = IPAddress.GetAddressBytes();

        int length = bytes.Length << 3;
        if (Subnet < 0 || Subnet > length)
            throw new ArgumentException("Subnet less 0 or greater than length of bytes", "str");

        for (i = Subnet + 1; i <= length; i++)
        {
            int bit = bytes[i - 1 >> 3] >> (8 - i & 7) & 1;
            if (bit != 0)
                throw new ArgumentException("Tail bytes not zero", "str");
        }
    }

    public int CompareTo(Cidr other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        if (IsIPv6 != other.IsIPv6)
        {
            if (IsIPv6)
                return 1;
            else
                return -1;
        }
        else
        {
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] > other.bytes[i])
                    return 1;
                else if (bytes[i] < other.bytes[i])
                    return -1;
        }
        if (Subnet > other.Subnet)
            return 1;
        else if (Subnet < other.Subnet)
            return -1;
        return 0;
    }

    public static implicit operator Cidr(IPAddress ip)
    {
        return new Cidr
        {
            IPAddress = ip,
            bytes = ip.GetAddressBytes(),
            Subnet = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128
        };
    }

    public static bool operator ==(Cidr one, Cidr two)
    {
        if (ReferenceEquals(one, null) || ReferenceEquals(null, two))
            return false;
        return ReferenceEquals(one, two)
            || one.Subnet == two.Subnet && one.IPAddress.Equals(two.IPAddress);
    }

    public static bool operator !=(Cidr one, Cidr two)
    {
        return !(one == two);
    }
}
