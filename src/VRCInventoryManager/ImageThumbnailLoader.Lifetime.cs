namespace VRCInventoryManager;

internal sealed partial class ImageThumbnailLoader
{
    public void Dispose()
    {
        lock (_operationGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _httpClient.Dispose();
            if (_activeOperations == 0)
            {
                DisposeGates();
            }
        }
    }

    private async Task<T> RunOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_operationGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _activeOperations++;
        }

        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            lock (_operationGate)
            {
                _activeOperations--;
                if (_isDisposed && _activeOperations == 0)
                {
                    DisposeGates();
                }
            }
        }
    }

    private void DisposeGates()
    {
        _localGate.Dispose();
        _remoteGate.Dispose();
    }
}
