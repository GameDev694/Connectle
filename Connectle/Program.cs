using Connectle.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Добавляем SignalR
builder.Services.AddSignalR();

// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin()  // Разрешаем все источники
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("CorsPolicy");
app.UseStaticFiles();
app.MapHub<ChatHub>("/chatHub");

// Для Render
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");