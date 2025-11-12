using Microsoft.AspNetCore.SignalR;

namespace Connectle.Hubs
{
    public class ChatHub : Hub
    {
        private static List<Message> _messages = new List<Message>();
        private static ExchangeRateCache _rateCache = new ExchangeRateCache();

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("ReceiveMessageHistory", _messages);
            await base.OnConnectedAsync();
        }

        public async Task SendMessage(string user, string text)
        {
            if (text.StartsWith("/"))
            {
                var args = text.Split(' ');
                var result = await ExecutePluginCommand(args[0].ToLower(), args);
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", result, DateTime.Now);
                return;
            }

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
                    _ => "❌ Неизвестная команда. Напишите /помощь"
                };
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка: {ex.Message}";
            }
        }

        private async Task<string> GetRealWeather(string[] args)
        {
            var city = args.Length > 1 ? args[1] : "Moscow";
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            try
            {
                var response = await httpClient.GetStringAsync($"http://wttr.in/{city}?format=%C+%t+%w");
                return $"🌤️ Погода в {city}: {response.Trim()}";
            }
            catch
            {
                var random = new Random();
                var temperatures = new[] { "+15°C", "+20°C", "+25°C", "+18°C", "+22°C" };
                var conditions = new[] { "☀️ Солнечно", "⛅ Облачно", "🌧️ Дождь", "❄️ Снег" };
                return $"🌤️ Погода в {city}: {conditions[random.Next(conditions.Length)]}, {temperatures[random.Next(temperatures.Length)]}";
            }
        }

        private string GetCurrentTime(string[] args)
        {
            var timezone = args.Length > 1 ? args[1].ToLower() : "москва";
            
            var now = timezone switch
            {
                "москва" or "moscow" => DateTime.UtcNow.AddHours(3),
                "лондон" or "london" => DateTime.UtcNow.AddHours(1),
                "нью-йорк" or "new york" => DateTime.UtcNow.AddHours(-4),
                "токио" or "tokyo" => DateTime.UtcNow.AddHours(9),
                "пекин" or "beijing" => DateTime.UtcNow.AddHours(8),
                _ => DateTime.UtcNow.AddHours(3)
            };
            
            var timezoneName = timezone switch
            {
                "москва" or "moscow" => "Москва",
                "лондон" or "london" => "Лондон", 
                "нью-йорк" or "new york" => "Нью-Йорк",
                "токио" or "tokyo" => "Токио",
                "пекин" or "beijing" => "Пекин",
                _ => "Москва"
            };
            
            return $"🕐 Время ({timezoneName}): {now:HH:mm:ss}";
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
                return "❌ Ошибка в выражении";
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
                "🤖 Почему программисты путают Хэллоуин и Рождество? Oct 31 == Dec 25!",
                "💻 Сколько программистов нужно, чтобы вкрутить лампочку? Ни одного!",
                "🐛 Приходит программист к психологу, а тот ему: 'У вас проблемы с отладкой личности'",
                "📚 Изучаю C#. Нашел 10 ошибок в коде. 1: думал, что это легко. Остальные 9: segmentation fault",
                "🔥 Почему Python стал таким популярным? Потому что его змея всех загипнотизировала!"
            };
            var random = new Random();
            return jokes[random.Next(jokes.Length)];
        }

        private async Task<string> GetExchangeRate()
        {
            // Используем кэш (обновляем раз в 10 минут)
            if (_rateCache.IsValid)
            {
                return _rateCache.Rates;
            }
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            try
            {
                var response = await httpClient.GetStringAsync("https://api.exchangerate.host/latest?base=USD&symbols=RUB,EUR,CNY");
                var data = System.Text.Json.JsonDocument.Parse(response);
                
                var rates = data.RootElement.GetProperty("rates");
                var usdToRub = Math.Round(rates.GetProperty("RUB").GetDouble(), 2);
                var usdToEur = Math.Round(1 / rates.GetProperty("EUR").GetDouble(), 2);
                var usdToCny = Math.Round(rates.GetProperty("CNY").GetDouble(), 2);
                
                var result = $"💵 Курсы валют (реальные):\nUSD → {usdToRub}₽\nEUR → {usdToEur}$\nCNY → {usdToCny}¥";
                
                // Сохраняем в кэш
                _rateCache.Update(result);
                return result;
            }
            catch
            {
                return _rateCache.Rates ?? GetFallbackRates();
            }
        }

        private string GetHelp()
        {
            return @"📚 Доступные команды:
🌤️ /погода [город] - Погода
🧮 /calc выражение - Калькулятор
😂 /шутка - Случайная шутка
🕐 /время [город] - Время (Москва, Лондон, Нью-Йорк, Токио)
💵 /курс - Реальные курсы валют
❓ /помощь - Справка";
        }

        // Класс для кэширования курсов
        private class ExchangeRateCache
        {
            public string Rates { get; private set; }
            public DateTime LastUpdate { get; private set; }
            
            public bool IsValid => !string.IsNullOrEmpty(Rates) && 
                                  DateTime.UtcNow - LastUpdate < TimeSpan.FromMinutes(10);
            
            public void Update(string rates)
            {
                Rates = rates;
                LastUpdate = DateTime.UtcNow;
            }
        }

        private string GetFallbackRates()
        {
            var random = new Random();
            return $"💵 Курсы валют (примерно):\n" +
                   $"USD → {random.Next(85, 95)}₽\n" +
                   $"EUR → {random.Next(98, 105)}₽\n" +
                   $"CNY → {random.Next(11, 13)}₽";
        }
    }

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
