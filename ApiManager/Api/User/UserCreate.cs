namespace UnoSysKernel
{
    using System.Threading.Tasks;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserCreateAsync(string userSessionToken, string userName, string password )
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("UserName", userName);
            ThrowIfParameterNullOrEmpty("Password", password);

            await identityManager.UserCreateAsync(new UserSessionToken(userSessionToken), userName, password).ConfigureAwait(false);
        }

        public void UserCreate(string userSessionToken, string userName, string password)
        {
            UserCreateAsync(userSessionToken, userName, password).Wait();
        }
    }
}
