namespace BigMission.RaceControlLog.Configuration
{
    internal class ConfigurationContext
    {
        private ConfigurationEventData data = new();
        private readonly SemaphoreSlim dataLock = new(1);

        public async Task UpdateConfiguration(ConfigurationEventData data, CancellationToken cancellationToken)
        {
            await dataLock.WaitAsync(cancellationToken);
            try
            {
                this.data = data;
            }
            finally
            {
                dataLock.Release();
            }
        }

        public async Task<ConfigurationEventData> GetConfiguration(CancellationToken cancellationToken)
        {
            await dataLock.WaitAsync(cancellationToken);
            try
            {
                return data;
            }
            finally
            {
                dataLock.Release();
            }
        }
    }
}
