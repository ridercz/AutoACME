using System;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Core.Challenges {
    public interface IChallengeHandler {
        Task CleanupAsync();
    }
}
