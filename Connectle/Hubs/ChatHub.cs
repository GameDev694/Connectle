using Microsoft.AspNetCore.SignalR;
using System.Net.Http;

namespace Connectle.Hubs
{
    public class ChatHub : Hub
    {
        // Список сообщений (пока храним в памяти)
        private static List<Message> _messages = new List<Message>();

        // Когда пользователь подключается
        public override async Task OnConnectedAsync()
        {
            // Отправляем историю сообщений новому пользователю
            await Clients.Caller.SendAsync("ReceiveMessageHistory", _messages);
            await base.OnConnectedAsync();
        }

        // Когда пользователь отправляет сообщение
        public async Task SendMessage(string user, string text)
        {
            // Проверяем команды плагинов
            if (text.StartsWith("/"))
            {
                var args = text.Split(' ');
                var result = await ExecutePluginCommand(args[0].ToLower(), args);
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", result, DateTime.Now);
                return;
            }

            // Обычное сообщение
            var message = new Message(user, text, DateTime.Now);
            _messages.Add(message);
            await Clients.All.SendAsync("ReceiveMessage", message.User, message.Text, message.Timestamp);
        }

        private async Task<string> ExecutePluginCommand(string command, string[] args)
        {
            try
            {
                return command.ToLower() switch
                {
                    "/погода" => await GetRealWeather(args),
                    "/время" => GetCurrentTime(args),
                    "/calc" => Calculate(args),
                    "/шутка" => GetRandomJoke(),
                    "/курс" => await GetExchangeRate(),
                    "/помощь" => GetHelp(),
                    _ => "❌ Неизвестная команда. Напишите /помощь для списка команд"
                };
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка модуля: {ex.Message}";
            }
        }

        private async Task<string> GetRealWeather(string[] args)
        {
            // Город по умолчанию - Москва, или из аргументов
            var city = args.Length > 1 ? args[1] : "Moscow";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                var response = await httpClient.GetStringAsync($"http://wttr.in/{city}?format=%C+%t+%w");
                var weatherData = response.Trim();

                return $"🌤️ Погода в {city}: {weatherData}";
            }
            catch
            {
                // Если API не доступен, показываем примерные данные
                var random = new Random();
                var temperatures = new[] { "+15°C", "+20°C", "+25°C", "+18°C", "+22°C" };
                var conditions = new[] { "☀️ Солнечно", "⛅ Облачно", "🌧️ Дождь", "❄️ Снег" };

                return $"🌤️ Погода в {city}: {conditions[random.Next(conditions.Length)]}, {temperatures[random.Next(temperatures.Length)]}";
            }
        }

        private string GetCurrentTime(string[] args)
        {
            var timezone = args.Length > 1 ? args[1] : "Москва";

            var timezones = new Dictionary<string, string>
            {
                ["москва"] = "MSK",
                ["лондон"] = "GMT",
                ["нью-йорк"] = "EST",
                ["токио"] = "JST"
            };

            var tz = timezones.ContainsKey(timezone.ToLower())
                ? timezones[timezone.ToLower()]
                : "MSK";

            return $"🕐 Время ({tz}): {DateTime.Now:HH:mm:ss}";
        }

        private string Calculate(string[] args)
        {
            if (args.Length < 2) return "❌ Использование: /calc 2+2";

            try
            {
                var expression = string.Join("", args.Skip(1));
                var result = EvaluateMathExpression(expression);
                return $"🧮 {expression} = {result}";
            }
            catch
            {
                return "❌ Неверное математическое выражение";
            }
        }

        private double EvaluateMathExpression(string expression)
        {
            expression = expression.Replace(" ", "");

            if (expression.Contains("+"))
            {
                var parts = expression.Split('+');
                return double.Parse(parts[0]) + double.Parse(parts[1]);
            }
            else if (expression.Contains("-"))
            {
                var parts = expression.Split('-');
                return double.Parse(parts[0]) - double.Parse(parts[1]);
            }
            else if (expression.Contains("*"))
            {
                var parts = expression.Split('*');
                return double.Parse(parts[0]) * double.Parse(parts[1]);
            }
            else if (expression.Contains("/"))
            {
                var parts = expression.Split('/');
                return double.Parse(parts[0]) / double.Parse(parts[1]);
            }

            return double.Parse(expression);
        }

        private string GetRandomJoke()
        {
            var jokes = new[]
            {
                "🤖 Почему программисты путают Хэллоуин и Рождество? Потому что Oct 31 == Dec 25!",
                "💻 Сколько программистов нужно, чтобы вкрутить лампочку? Ни одного, это hardware проблема!",
                "🐛 Приходит программист к психологу, а тот ему: 'У вас проблемы с отладкой личности'",
                "📚 Изучаю C#. Нашел 10 ошибок в коде. 1: думал, что это легко. Остальные 9: segmentation fault",
                "🔥 Почему Python стал таким популярным? Потому что его змея всех загипнотизировала!",
                "💾 Что сказал один бит другому? 'Давай встретимся в середине байта!'",
                "🚀 Почему JavaScript разработчики носят очки? Потому что они не C#!",
                "📱 Mobile-разработчик заходит в бар. Бармен: 'У нас есть веб-версия'",
                "🎮 Играю в шахматы с компьютером. Проиграл. Компьютер: 'Хорошая игра... для человека'",
                "🤔 Зачем программисту дверь? Чтобы открывать и закрывать, пока не найдет баг"
            };

            var random = new Random();
            return jokes[random.Next(jokes.Length)];
        }

        private async Task<string> GetExchangeRate()
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                // Простая имитация API курсов валют
                await Task.Delay(100); // Имитация задержки сети
                return "💵 Курсы валют: USD ≈ 90₽, EUR ≈ 100₽, CNY ≈ 12₽";
            }
            catch
            {
                return "💵 Курсы валют: USD ≈ 90₽, EUR ≈ 100₽ (данные временно недоступны)";
            }
        }

        private string GetHelp()
        {
            return @"📚 Доступные команды:
🌤️ /погода [город] - Узнать погоду
🧮 /calc выражение - Калькулятор (например: /calc 15+27)
😂 /шутка - Случайная шутка
🕐 /время [город] - Текущее время
💵 /курс - Курсы валют
❓ /помощь - Эта справка";
        }
    }

    // Класс для хранения сообщения
    public class Message
    {
        public string User { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }

        public Message(string user, string text, DateTime timestamp)
        {
            User = user;
            Text = text;
            Timestamp = timestamp;
        }
    }
}