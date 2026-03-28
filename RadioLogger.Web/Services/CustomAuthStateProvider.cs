using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Security.Claims;

namespace RadioLogger.Web.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ProtectedSessionStorage _sessionStorage;
        private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

        public CustomAuthStateProvider(ProtectedSessionStorage sessionStorage)
        {
            _sessionStorage = sessionStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var result = await _sessionStorage.GetAsync<string>("username");
                var roleResult = await _sessionStorage.GetAsync<string>("role");
                var displayResult = await _sessionStorage.GetAsync<string>("displayName");

                if (result.Success && !string.IsNullOrEmpty(result.Value))
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, result.Value),
                        new Claim(ClaimTypes.Role, roleResult.Value ?? "Operador"),
                        new Claim("DisplayName", displayResult.Value ?? result.Value)
                    };
                    var identity = new ClaimsIdentity(claims, "CustomAuth");
                    _currentUser = new ClaimsPrincipal(identity);
                }
            }
            catch
            {
                // Session storage no disponible (prerender)
                _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            }

            return new AuthenticationState(_currentUser);
        }

        public async Task LoginAsync(string username, string role, string displayName)
        {
            await _sessionStorage.SetAsync("username", username);
            await _sessionStorage.SetAsync("role", role);
            await _sessionStorage.SetAsync("displayName", displayName);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
                new Claim("DisplayName", displayName)
            };
            var identity = new ClaimsIdentity(claims, "CustomAuth");
            _currentUser = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public async Task LogoutAsync()
        {
            await _sessionStorage.DeleteAsync("username");
            await _sessionStorage.DeleteAsync("role");
            await _sessionStorage.DeleteAsync("displayName");

            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }
}
