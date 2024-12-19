namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> VirtualDiskMountAsync(string userSessionToken, string virtualDiskSessionToken, uint blockSize)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            //ThrowIfParameterNoValidIDString("VirtualDiskSessionToken", virtualDiskSessionToken);
            ThrowIfParameterNoValidSessionTokenString("VirtualDiskSessionToken", virtualDiskSessionToken, SessionType.VirtualDisk);
            ThrowIfParameterNotInInclusiveIntegerRange("BlockSize", Convert.ToInt32(blockSize), 512, (64 * 1024));  // %TODO% - USE Constants here...MIN_VDISK_BLOCKSIZE & MAX_VDISK_BLOCKSIZE
            if( blockSize % 512 != 0)
            {
                throw new UnoSysArgumentException("BlockSize not a multiple of 512");
            }
            var ust = new UserSessionToken(userSessionToken);
            

            //if (!wcContext.CheckResourceOwnerContext(ust, rst))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}

            return await virtualDiskManager.MountAsync(virtualDiskSessionToken, blockSize).ConfigureAwait(false);
        }

        public string VirtualDiskMount(string userSessionToken, string virtualDiskSessionToken, uint blockSize )
        {
            return VirtualDiskMountAsync(userSessionToken, virtualDiskSessionToken, blockSize).Result;
        }
    }
}