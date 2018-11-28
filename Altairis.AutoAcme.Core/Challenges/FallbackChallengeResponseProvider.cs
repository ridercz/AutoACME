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
            Log.WriteLine("Validate via "+provider.ChallengeType+"...");
            Log.Indent();
            try {
                return provider.ValidateAsync(context, authorizationContexts);
            }
            finally {
                Log.Unindent();
            }
        }

        public async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            index = 0;
            while (index < providers.Length) {
                var provider = providers[index];
                Log.WriteLine($"Testing {provider.ChallengeType}...");
                Log.Indent();
                try {
                    if (await provider.TestAsync(hostNames).ConfigureAwait(true)) {
                        return true;
                    }
                }
                finally {
                    Log.Unindent();
                }
                index++;
            }
            return false;
        }
    }
}
