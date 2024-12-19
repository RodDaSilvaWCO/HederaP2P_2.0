namespace UnoSysKernel
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> VirtualDiskVolumeDataOperationAsync(string userSessionToken, string base64Operation )
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("Operation", base64Operation);
            var ust = new UserSessionToken(userSessionToken);


            //if (!wcContext.CheckResourceOwnerContext(ust, rst))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            try
            {
                var rawOperationbytes = Convert.FromBase64String(base64Operation);
                VolumeDataOperation operation = new VolumeDataOperation(rawOperationbytes);
                return JsonSerializer.Serialize(await virtualDiskManager.VolumeDataOperationAsync(operation).ConfigureAwait(false) );
            }
            catch (Exception)
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            
        }

        public string VirtualDiskVolumeDataOperation(string userSessionToken, string base64Operation)
        {
            return VirtualDiskVolumeDataOperationAsync(userSessionToken, base64Operation).Result;
        }
    }
}