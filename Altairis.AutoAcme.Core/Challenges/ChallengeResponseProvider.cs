using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace Altairis.AutoAcme.Core.Challenges {
    public abstract class ChallengeResponseProvider: IDisposable {
        protected ChallengeResponseProvider(bool verboseMode) {
            VerboseMode = verboseMode;
        }

        public bool VerboseMode { get; }

        public abstract string ChallengeType { get; }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }

        protected abstract Task<IDisposable> CreateChallengeHandler(IChallengeContext ch, string hostName, IKey accountKey);

        public async Task<bool> ValidateAsync(AutoAcmeContext context, IEnumerable<IAuthorizationContext> authorizationContexts) {
            // Get challenge
            Trace.Write("Getting challenge...");
            var records = new List<IDisposable>();
            var result = true;
            try {
                // Prepare challenges
                var challenges = new Dictionary<Uri, IChallengeContext>();
                foreach (var authorizationContext in authorizationContexts) {
                    var authorization = await authorizationContext.Resource().ConfigureAwait(false);
                    Trace.WriteLine("OK, the following is DNS name:");
                    Trace.WriteLine(authorization.Identifier.Value);
                    var ch = await authorizationContext.Challenge(ChallengeType).ConfigureAwait(false);
                    var challenge = await CreateChallengeHandler(ch, authorization.Identifier.Value, context.AccountKey).ConfigureAwait(false);
                    records.Add(challenge);
                    challenges.Add(ch.Location, ch);
                }
                Trace.Write("Completing challenge");
                for (var i = 0; i < context.ChallengeVerificationRetryCount; i++) {
                    Trace.Write(".");
                    foreach (var challenge in await Task.WhenAll(challenges.Values.Select(ch => ch.Validate())).ConfigureAwait(false)) {
                        switch (challenge.Status) {
                            case ChallengeStatus.Invalid:
                                Trace.WriteLine($"Challenge {challenge.Status}: {challenge.Url} {challenge.Error?.Detail}");
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
                }
                // Complete challenge
                Trace.WriteLine(result ? "OK" : "Failed");
                return result;
            }
            catch (Exception ex) {
                Trace.WriteLine("Challenge exception:");
                Trace.WriteLine(ex.ToString());
                return false;
            }
            finally {
                foreach (var record in records) {
                    record.Dispose();
                }
            }
        }

        public abstract Task<bool> TestAsync(IEnumerable<string> hostNames);
    }
}
