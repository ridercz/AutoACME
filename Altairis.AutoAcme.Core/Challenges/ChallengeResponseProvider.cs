using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace Altairis.AutoAcme.Core.Challenges {
    public abstract class ChallengeResponseProvider : IChallengeResponseProvider {
        public abstract string ChallengeType { get; }

        public void Dispose() {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }

        protected abstract Task<IChallengeHandler> CreateChallengeHandlerAsync(IChallengeContext ch, string hostName, IKey accountKey);

        public async Task<bool> ValidateAsync(AutoAcmeContext context, IEnumerable<IAuthorizationContext> authorizationContexts) {
            // Get challenge
            Log.WriteLine("Getting challenge...");
            var handlers = new List<IChallengeHandler>();
            var result = true;
            try {
                // Prepare challenges
                var challenges = new Dictionary<Uri, IChallengeContext>();
                Log.Indent();
                foreach (var authorizationContext in authorizationContexts) {
                    var authorization = await authorizationContext.Resource().ConfigureAwait(false);
                    Log.WriteLine("OK, the following is DNS name:");
                    Log.Indent();
                    Log.WriteLine(authorization.Identifier.Value);
                    var ch = await authorizationContext.Challenge(this.ChallengeType).ConfigureAwait(false);
                    var handler = await this.CreateChallengeHandlerAsync(ch, authorization.Identifier.Value, context.AccountKey).ConfigureAwait(false);
                    Log.Unindent();
                    handlers.Add(handler);
                    challenges.Add(ch.Location, ch);
                }
                Log.Unindent();
                Log.Write("Completing challenge");
                var challengeTasks = challenges.Values.Select(ch => ch.Validate());
                for (var i = 0; i < context.ChallengeVerificationRetryCount; i++) {
                    Log.Write(".");
                    foreach (var challenge in await Task.WhenAll(challengeTasks).ConfigureAwait(false)) {
                        switch (challenge.Status) {
                            case ChallengeStatus.Invalid:
                                Log.WriteLine($"Challenge {challenge.Status}: {challenge.Url} {challenge.Error?.Detail}");
                                challenges.Remove(challenge.Url);
                                result = false;
                                break;
                            case ChallengeStatus.Valid:
                                challenges.Remove(challenge.Url);
                                break;
                        }
                    }
                    if (challenges.Count == 0) {
                        break;
                    }
                    await Task.Delay(context.ChallengeVerificationWait).ConfigureAwait(false);
                    challengeTasks = challenges.Values.Select(ch => ch.Resource());
                }
                // Complete challenge
                Log.WriteLine(result ? "OK" : "Failed");
                return result;
            }
            catch (Exception ex) {
                Log.WriteLine("Challenge exception:");
                Log.WriteLine(ex.ToString());
                return false;
            }
            finally {
                foreach (var handler in handlers) {
                    try {
                        await handler.CleanupAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        Log.WriteLine("Error on challenge response disposal (maybe requires manual cleanup): " + ex.Message);
                    }
                }
            }
        }

        public abstract Task<bool> TestAsync(IEnumerable<string> hostNames);
    }
}
