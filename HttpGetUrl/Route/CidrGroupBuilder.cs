namespace HttpGetUrl.Route;

public class CidrGroupBuilder
{
    private readonly List<Cidr> cidrs = [];

    public void AddCidrs(IEnumerable<Cidr> cidrs)
    {
        this.cidrs.AddRange(cidrs);
    }

    public CidrGroup Build()
    {
        cidrs.Sort();
        var queue = new Queue<Cidr>(cidrs);
        var stack = new Stack<Cidr>();

        while (queue.Count != 0)
        {
            if (stack.Count == 0 || stack.Peek().IsIPv6 != queue.Peek().IsIPv6)
            {
                stack.Push(queue.Dequeue());
            }
            else if (stack.Peek().ExistsIntersection(queue.Peek()))
            {
                queue.Dequeue();
            }
            else if (stack.Peek().Subnet > 0 && stack.Peek().IsPaired(queue.Peek()))
            {
                stack.Pop();
                stack.Push(queue.Dequeue().GetBiggerSubnet());
            }
            else
            {
                stack.Push(queue.Dequeue());
            }
        }

        return new CidrGroup(stack.Reverse().ToArray());
    }
}
