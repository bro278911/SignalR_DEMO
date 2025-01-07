using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using signalR_test.Hubs;
using signalR_test.Services;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 102400; // 100 KB
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
});

// 移除舊的監控服務
//builder.Services.AddHostedService<DatabaseMonitorService>();

// 配置日誌
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// 配置 CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyHeader()
               .AllowAnyMethod()
               .SetIsOriginAllowed((host) => true)
               .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

//app.UseHttpsRedirection(); // 在開發環境中暫時禁用 HTTPS 重定向
app.UseStaticFiles();
app.UseRouting();

// 啟用 CORS
app.UseCors();

app.UseAuthorization();

// 配置端點
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=SqlTest}/{id?}");

app.MapHub<ChatHub>("/chatHub");

app.Run();
