namespace UnoSysKernel
{
    //using Oqtane.Shared;
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserChangePasswordAsync(string userSessionToken, string userName, string oldPassword, string newPassword )
        {
            // NOTE:  **** This API is only intended to be called internally within the WorldComputer
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("UserName", userName);
            ThrowIfParameterNullOrEmpty("OldPassword", oldPassword);
            ThrowIfParameterNullOrEmpty("NewPassword", newPassword);
            ThrowIfEqual("'OldPassword' & 'NewPassword'", oldPassword, newPassword);
            await identityManager.UserChangePasswordAsync(new UserSessionToken(userSessionToken), userName, oldPassword, newPassword).ConfigureAwait(false);
        }

        public void UserChangePassword(string userSessionToken, string userName, string oldPassword, string newPassword)
        {
            UserChangePasswordAsync(userSessionToken, userName, oldPassword, newPassword).Wait();
        }
    }
}
