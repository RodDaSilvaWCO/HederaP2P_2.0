namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;
    using UnoSysCore;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task VirtualDiskDeleteAsync(string userSessionToken, string virtualDiskSessionToken)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNoValidSessionTokenString("VirtualDiskSessionToken", virtualDiskSessionToken, SessionType.VirtualDisk);
            var ust = new UserSessionToken(userSessionToken);
            

            //if (!wcContext.CheckResourceOwnerContext(ust, rst))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}

            await virtualDiskManager.DeleteAsync(virtualDiskSessionToken).ConfigureAwait(false);
        }

        public void VirtualDiskDelete(string userSessionToken, string virtualDiskSessionToken)
        {
            VirtualDiskDeleteAsync(userSessionToken, virtualDiskSessionToken).Wait();
        }
    }
}