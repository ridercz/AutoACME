using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Certes.Acme;

namespace Altairis.AutoAcme.Core.Challenges {
    public interface IChallengeResponseProvider : IDisposable {
        Task<bool> ValidateAsync(AutoAcmeContext context, IEnumerable<IAuthorizationContext> authorizationContexts);

        Task<bool> TestAsync(IEnumerable<string> hostNames);
    }
}