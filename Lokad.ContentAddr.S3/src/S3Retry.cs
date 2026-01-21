using Amazon.S3;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> Deal with transient S3 failures. </summary>
    public static class S3Retry
    {
        /// <summary> Maximum time for which we retry requests to S3. </summary>
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

        /// <returns> True if we should retry on this error. </returns>
        private static bool ShouldRetry(Exception ex)
        {
            if (ex is AmazonS3Exception s3)
            {
                if ((int)s3.StatusCode >= 500)
                    return true;

                if (s3.StatusCode == HttpStatusCode.RequestTimeout)
                    return true;

                if (s3.ErrorCode == "SlowDown" || s3.ErrorCode == "RequestTimeout")
                    return true;
            }

            if (ex is OperationCanceledException)
                return false;

            if (ex is HttpRequestException)
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
                catch (Exception e) when (DateTime.UtcNow < until && ShouldRetry(e))
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
            catch (Exception e) when (ShouldRetry(e))
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
                catch (Exception e) when (DateTime.UtcNow < until && ShouldRetry(e))
                {
                    OnRetry?.Invoke(e);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false);
                }
            }
        }
    }
}
