using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Certes.Acme;

namespace Altairis.AutoAcme.Core.Challenges {
    public class FallbackChallengeResponseProvider: IChallengeResponseProvider {
        private readonly ChallengeResponseProvider[] providers;
        private int index;

        public FallbackChallengeResponseProvider(params ChallengeResponseProvider[] providers) { this.providers = providers; }

        public void Dispose() { Array.ForEach(providers, provider => provider.Dispose()); }

        public Task<bool> ValidateAsync(AutoAcmeContext context, IEnumerable<IAuthorizationContext> authorizationContexts) {
            if (index >= providers.Length) {
                return Task.FromResult(false);
            }
            var provider = providers[index];
            Trace.Write("Validate via "+provider.ChallengeType+"...");
            return provider.ValidateAsync(context, authorizationContexts);
        }

        public async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            index = 0;
            while (index < providers.Length) {
                var provider = providers[index];
                Trace.Write("Testing "+provider.ChallengeType+"...");
                if (await provider.TestAsync(hostNames).ConfigureAwait(false)) {
                    return true;
                }
                index++;
            }
            return false;
        }
    }
}
