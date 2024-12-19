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
        public async Task VirtualDiskUnmountAsync(string userSessionToken, string volumeSessionToken)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            //ThrowIfParameterNoValidIDString("VolumeSessionToken", volumeSessionToken);
            ThrowIfParameterNoValidSessionTokenString("VolumeSessionToken", volumeSessionToken, SessionType.Volume);
            var ust = new UserSessionToken(userSessionToken);
            

            //if (!wcContext.CheckResourceOwnerContext(ust, rst))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}

            await virtualDiskManager.UnmountAsync(volumeSessionToken).ConfigureAwait(false);
        }

        public void VirtualDiskUnmount(string userSessionToken, string volumeSessionToken)
        {
            VirtualDiskUnmountAsync(userSessionToken, volumeSessionToken).Wait();
        }
    }
}