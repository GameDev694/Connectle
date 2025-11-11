using Connectle.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Добавляем SignalR
builder.Services.AddSignalR();

// Настройка CORS - ИСПРАВЛЕННАЯ ВЕРСИЯ
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)  // Вместо AllowAnyOrigin()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Используем настройки CORS
app.UseCors("CorsPolicy");

// Разрешаем обслуживание статических файлов
app.UseStaticFiles();

// Настраиваем маршруты
app.MapHub<ChatHub>("/chatHub");

// Запускаем сервер
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");
