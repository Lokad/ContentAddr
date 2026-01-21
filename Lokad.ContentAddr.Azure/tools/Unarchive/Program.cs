using Lokad.ContentAddr;
using Lokad.ContentAddr.Azure;
using Lokad.Secrets;

namespace Unarchive
{
    public static class Program
    {
        static async Task Main()
        {
            Console.WriteLine("Lokad.ContentAddr.Azure archive restore tool");
            Console.WriteLine("Enter connection string:");
            if (Console.ReadLine() is not string connStr) return;

            var secret = await LokadSecrets.ResolveAsync(connStr.Trim(), default);
            var factory = new AzureStoreFactory(secret.Value);

            try
            {
                var accounts = await factory.GetAccountsAsync(default);
                Console.WriteLine($"Found {accounts.Count} accounts in store");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error when connecting: {e.Message}");
                return;
            }

            Console.WriteLine("Enter account/hash, one per line, to restore an archived blob");
            Console.WriteLine("Enter 'quit' to quit (will wait for all restorations to complete)");

            var todo = 0;

            while (true)
            {
                if (Console.ReadLine() is not string line || line.Trim() == "quit")
                {
                    if (todo == 0) return;
                    Console.WriteLine($"Waiting for {todo} background blob restorations to complete");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                var split = line.Trim().Split('/');
                if (split.Length != 2 || !long.TryParse(split[0], out var account) || !Hash.TryParse(split[1], out var hash))
                {
                    Console.WriteLine($"Malformed account/hash value: {line}");
                    continue;
                }

                Console.WriteLine($"Restoring {account}/{hash} in the background ({todo + 1} blobs being restored in total)");

                _ = Restore(account, hash);
            }

            while (todo > 0)
                await Task.Delay(TimeSpan.FromSeconds(10));

            async Task Restore(long account, Hash hash)
            {
                Interlocked.Increment(ref todo);
                try
                {
                    var store = (AzureStore)factory[account];
                    while (true)
                    {
                        var status = await store.TryUnArchiveBlobAsync(hash);
                        switch (status)
                        {
                            case UnArchiveStatus.DoesNotExist:
                                Console.WriteLine($"Cannot restore {account}/{hash}: archive blob does not exist.");
                                return;
                            case UnArchiveStatus.Rehydrating:
                                await Task.Delay(TimeSpan.FromSeconds(10));
                                continue;
                            case UnArchiveStatus.Done:
                                var blob = store[hash];
                                var size = await blob.GetSizeAsync(default);
                                Console.WriteLine($"Restored {account}/{hash} ({size} bytes).");
                                return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cannot restore {account}/{hash}: {ex.Message}");
                }
                finally
                {
                    var remain = Interlocked.Decrement(ref todo);
                    Console.WriteLine($"{remain} blobs still being restored in the background");
                }
            }
        }
    }
}