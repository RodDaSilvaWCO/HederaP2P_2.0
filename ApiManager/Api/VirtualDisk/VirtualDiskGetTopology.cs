namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> VirtualDiskGetTopologyAsync(string userSessionToken, string virtualDiskSessionToken)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNoValidSessionTokenString("VirtualDiskSessionToken", virtualDiskSessionToken, SessionType.VirtualDisk );
            
            var ust = new UserSessionToken(userSessionToken);


            //if (!wcContext.CheckResourceOwnerContext(ust, rst))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}

            return await virtualDiskManager.GetTopologyAsync(virtualDiskSessionToken).ConfigureAwait(false);
        }

        public string VirtualDiskGetTopology(string userSessionToken, string virtualDiskSessionToken)
        {
            return VirtualDiskGetTopologyAsync(userSessionToken, virtualDiskSessionToken).Result;
        }
    }
}