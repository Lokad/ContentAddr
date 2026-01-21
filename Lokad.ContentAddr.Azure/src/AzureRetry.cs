using Azure;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> Deal with Azure downtime. </summary>
    public static class AzureRetry
    {
        /// <summary> Maximum time for which we retry requests to Azure. </summary>
        public static TimeSpan MaxRetries { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        ///     Maximum time allowed for an action before it is ignored and 
        ///     retried.
        /// </summary>
        public static TimeSpan MaxDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <see cref="OnException"/>
        public delegate void ExceptionLogger(Exception e);

        /// <summary>
        ///     Whenever an exception triggers an automatic retry.
        /// </summary>
        public static event ExceptionLogger OnRetry;

        /// <summary>
        ///     There is an Azure Storage glitch where, for a few minutes, a large proportion
        ///     of properly authenticated requests is rejected with a 403. If this property
        ///     is set, actions encountering a 403 rejection will be retried for the standard
        ///     duration. Note that if the 403 rejection is legitimate (e.g. expired credentials)
        ///     this will delay the reporting of the issue. 
        /// </summary>
        public static bool RetryOn403 { get; set; } = false;

        /// <summary>
        ///     Sometimes, a DNS problem prevents the blob storage domain from being 
        ///     resolved. If this property is true, encountering this error will be retried
        ///     for the standard duration. Note that if the error is legitimate (e.g. bad 
        ///     configuratino) this will delay the reporting of the issue.
        /// </summary>
        public static bool RetryOnNoSuchAddress { get; set; } = false;

        /// <returns> True if we should retry on this error. </returns>
        private static bool ShouldRetry(RequestFailedException e)
        {
            if (e.Status >= 500)
                // All server errors in the Blob Storage can be hoped to be transient,
                // so keep retrying.
                return true;

            if (RetryOn403 && e.Status == 403
                           && e.ErrorCode == "AuthenticationFailed")
                return true;

            if (RetryOnNoSuchAddress && e.Message == "No such device or address")
                return true;

            if (e.Message == "The SSL connection could not be established, see inner exception.")
                return true;

            if (e.Status == 404 &&
                e.ErrorCode != "BlobNotFound" &&
                e.ErrorCode != "CannotVerifyCopySource" &&
                e.ErrorCode != "ResourceNotFound" &&
                e.ErrorCode != "ContainerNotFound")
                // Sometimes, the Azure API glitches out and returns an unrelated 404,
                // which is fixed by waiting and retrying.
                return true;

            return false;
        }

        /// <summary>
        ///     Retries on all retry-able failures until a result can be returned or 
        ///     the maximum time is reached.
        /// </summary>
        public static async Task<T> Do<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancel)
        {
            var until = DateTime.UtcNow + MaxRetries;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                try
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                    {
                        cts.CancelAfter(MaxDuration);

                        var task = action(cts.Token);
                        await Task.WhenAny(task, Task.Delay(MaxDuration, cts.Token)).ConfigureAwait(false);

                        var timedOut = !cancel.IsCancellationRequested && cts.Token.IsCancellationRequested;

                        if (task.IsCompleted && !timedOut)
                            return await task.ConfigureAwait(false);

                        if (DateTime.UtcNow >= until)
                            throw new OperationCanceledException($"Retried for more than {MaxRetries} without success.");

                        OnRetry?.Invoke(new OperationCanceledException(cts.Token));
                    }
                }
                catch (RequestFailedException e) when (DateTime.UtcNow < until && ShouldRetry(e))
                {
                    OnRetry?.Invoke(e);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        ///     If a retry-able failure occurs, returns false, otherwise returns the result
        ///     of the action. No retries involved !
        /// </summary>
        public static async Task<bool> OrFalse(Func<Task<bool>> action)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }

            catch (RequestFailedException e) when (ShouldRetry(e))
            {
                OnRetry?.Invoke(e);
                return false;
            }
        }

        /// <summary>
        ///     Retries on all retry-able failures until the action completes or 
        ///     the maximum time is reached.
        /// </summary>
        public static async Task Do(Func<CancellationToken, Task> action, CancellationToken cancel)
        {
            var until = DateTime.UtcNow + MaxRetries;

            while (true)
            {
                try
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                    {
                        cts.CancelAfter(MaxDuration);

                        var task = action(cts.Token);
                        await Task.WhenAny(task, Task.Delay(MaxDuration, cts.Token)).ConfigureAwait(false);

                        var timedOut = !cancel.IsCancellationRequested && cts.Token.IsCancellationRequested;

                        if (task.IsCompleted && !timedOut)
                        {
                            await task.ConfigureAwait(false);
                            return;
                        }

                        if (DateTime.UtcNow >= until)
                            throw new OperationCanceledException($"Retried for more than {MaxRetries} without success.");

                        OnRetry?.Invoke(new OperationCanceledException(cts.Token));
                    }
                }
                catch (RequestFailedException e) when (DateTime.UtcNow < until && ShouldRetry(e))
                {
                    OnRetry?.Invoke(e);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false);
                }
            }
        }
    }
}
