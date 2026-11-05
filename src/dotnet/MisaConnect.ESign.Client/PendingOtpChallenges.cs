namespace MisaConnect.ESign.Client;

/// <summary>
/// Bounded, thread-safe set of principals (clientId + userName) that currently have
/// an outstanding two-factor challenge. Oldest entries are evicted past capacity.
/// </summary>
internal sealed class PendingOtpChallenges
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly LinkedList<(string ClientId, string UserName)> _order = new();

    public PendingOtpChallenges(int capacity = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public void Add(string clientId, string userName)
    {
        lock (_lock)
        {
            RemoveCore(clientId, userName);
            _order.AddLast((clientId, userName));
            while (_order.Count > _capacity)
            {
                _order.RemoveFirst();
            }
        }
    }

    public bool Contains(string clientId, string userName)
    {
        lock (_lock)
        {
            return Find(clientId, userName) is not null;
        }
    }

    public void Remove(string clientId, string userName)
    {
        lock (_lock)
        {
            RemoveCore(clientId, userName);
        }
    }

    private void RemoveCore(string clientId, string userName)
    {
        if (Find(clientId, userName) is { } node)
        {
            _order.Remove(node);
        }
    }

    private LinkedListNode<(string ClientId, string UserName)>? Find(string clientId, string userName)
    {
        for (var n = _order.First; n is not null; n = n.Next)
        {
            if (string.Equals(n.Value.ClientId, clientId, StringComparison.Ordinal)
                && string.Equals(n.Value.UserName, userName, StringComparison.Ordinal))
            {
                return n;
            }
        }
        return null;
    }
}
