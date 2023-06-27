using BigMission.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BigMission.AlarmProcessor
{
    public class Startup
    {
        public Startup() { }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IDateTimeHelper, DateTimeHelper>();
            //services.AddSingleton(serviceStatus);
            services.AddHostedService<Application>();
            services.AddHealthChecks().AddCheck<ServiceHealthCheck>("service");
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/startup");
                endpoints.MapHealthChecks("/liveness");
                endpoints.MapHealthChecks("/ready");
                endpoints.MapControllers();
            });
        }
    }
}
