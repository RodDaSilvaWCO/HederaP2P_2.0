namespace UnoSysKernel
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> SimulationVisualizationAsync( )
        {
            try
            {
                // %TODO%
            }
            catch(Exception ex)
            {
                Debug.Print($"*** Error in ApiManager.SimulationVisualizationAsync() - {ex}");
                throw;
            }
            //return await spawnManager.SpawnNodeAsync().ConfigureAwait(false);
            return await Task.FromResult("OK");
        }

        public string SimulationVisualization()
        {
            return SimulationVisualizationAsync().Result;
        }
    }
}
