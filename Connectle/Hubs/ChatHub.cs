using Microsoft.AspNetCore.SignalR;
using System;
using System.Net.Http;
using System.Text.Json;

namespace Connectle.Hubs
{
    public class ChatHub : Hub
    {
        private static List<Message> _messages = new List<Message>();

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
                return $"🌤️ Погода в {city}: Данные временно недоступны";
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
            if (args.Length < 2) return "❌ Использование: /calc выражение\nПример: /calc sin(pi/2) + log(100)";
            
            try
            {
                var expression = string.Join(" ", args.Skip(1));
                var result = EvaluateScientificExpression(expression);
                return $"🧮 {expression} = {result}";
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка: {ex.Message}\n💡 Используйте: sin, cos, tan, log, ln, sqrt, pi, e, ^";
            }
        }

        private double EvaluateScientificExpression(string expression)
        {
            expression = expression.ToLower()
                .Replace("pi", Math.PI.ToString())
                .Replace("e", Math.E.ToString())
                .Replace(" ", "")
                .Replace(",", ".");
            
            // Обрабатываем скобки и функции
            while (expression.Contains('(') && expression.Contains(')'))
            {
                var openBracket = expression.LastIndexOf('(');
                var closeBracket = expression.IndexOf(')', openBracket);
                
                if (closeBracket == -1) 
                    throw new ArgumentException("Непарные скобки");
                    
                var innerExpression = expression.Substring(openBracket + 1, closeBracket - openBracket - 1);
                var innerResult = EvaluateScientificExpression(innerExpression);
                
                // Проверяем функции перед скобками
                var functionStart = Math.Max(0, openBracket - 4);
                var beforeBracket = expression.Substring(functionStart, openBracket - functionStart);
                
                if (beforeBracket.EndsWith("sin"))
                {
                    innerResult = Math.Sin(innerResult);
                    expression = expression.Substring(0, openBracket - 3) + innerResult + expression.Substring(closeBracket + 1);
                }
                else if (beforeBracket.EndsWith("cos"))
                {
                    innerResult = Math.Cos(innerResult);
                    expression = expression.Substring(0, openBracket - 3) + innerResult + expression.Substring(closeBracket + 1);
                }
                else if (beforeBracket.EndsWith("tan"))
                {
                    innerResult = Math.Tan(innerResult);
                    expression = expression.Substring(0, openBracket - 3) + innerResult + expression.Substring(closeBracket + 1);
                }
                else if (beforeBracket.EndsWith("log"))
                {
                    innerResult = Math.Log10(innerResult);
                    expression = expression.Substring(0, openBracket - 3) + innerResult + expression.Substring(closeBracket + 1);
                }
                else if (beforeBracket.EndsWith("ln"))
                {
                    innerResult = Math.Log(innerResult);
                    expression = expression.Substring(0, openBracket - 2) + innerResult + expression.Substring(closeBracket + 1);
                }
                else if (beforeBracket.EndsWith("sqrt"))
                {
                    innerResult = Math.Sqrt(innerResult);
                    expression = expression.Substring(0, openBracket - 4) + innerResult + expression.Substring(closeBracket + 1);
                }
                else
                {
                    expression = expression.Substring(0, openBracket) + innerResult + expression.Substring(closeBracket + 1);
                }
            }
            
            return EvaluateSimpleExpression(expression);
        }

        private double EvaluateSimpleExpression(string expression)
        {
            // Степень
            for (int i = expression.Length - 1; i >= 0; i--)
            {
                if (expression[i] == '^')
                {
                    var left = EvaluateSimpleExpression(expression.Substring(0, i));
                    var right = EvaluateSimpleExpression(expression.Substring(i + 1));
                    return Math.Pow(left, right);
                }
            }
            
            // Умножение и деление
            for (int i = expression.Length - 1; i >= 0; i--)
            {
                if (expression[i] == '*')
                {
                    var left = EvaluateSimpleExpression(expression.Substring(0, i));
                    var right = EvaluateSimpleExpression(expression.Substring(i + 1));
                    return left * right;
                }
                else if (expression[i] == '/')
                {
                    var left = EvaluateSimpleExpression(expression.Substring(0, i));
                    var right = EvaluateSimpleExpression(expression.Substring(i + 1));
                    if (right == 0) throw new ArgumentException("Деление на ноль");
                    return left / right;
                }
            }
            
            // Сложение и вычитание
            for (int i = expression.Length - 1; i >= 0; i--)
            {
                if (expression[i] == '+')
                {
                    var left = EvaluateSimpleExpression(expression.Substring(0, i));
                    var right = EvaluateSimpleExpression(expression.Substring(i + 1));
                    return left + right;
                }
                else if (expression[i] == '-' && i > 0)
                {
                    var left = EvaluateSimpleExpression(expression.Substring(0, i));
                    var right = EvaluateSimpleExpression(expression.Substring(i + 1));
                    return left - right;
                }
            }
            
            return double.Parse(expression, System.Globalization.CultureInfo.InvariantCulture);
        }

        private string GetRandomJoke()
        {
            var jokes = new[]
            {
                "🤖 Почему программисты путают Хэллоуин и Рождество? Oct 31 == Dec 25!",
                "💻 Сколько программистов нужно, чтобы вкрутить лампочку? Ни одного!",
                "🐛 Приходит программист к психологу, а тот ему: 'У вас проблемы с отладкой личности'",
                "📚 Изучаю C#. Нашел 10 ошибок в коде. 1: думал, что это легко. Остальные 9: segmentation fault",
                "🔥 Почему Python стал таким популярным? Потому что его змея всех загипнотизировала!",
                "💾 Что сказал один бит другому? 'Давай встретимся в середине байта!'"
            };
            var random = new Random();
            return jokes[random.Next(jokes.Length)];
        }

        private async Task<string> GetExchangeRate()
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            try
            {
                // API Центробанка России - реальные курсы
                var response = await httpClient.GetStringAsync("https://www.cbr-xml-daily.ru/daily_json.js");
                var data = JsonDocument.Parse(response);
                
                var valute = data.RootElement.GetProperty("Valute");
                
                var usdRate = Math.Round(valute.GetProperty("USD").GetProperty("Value").GetDouble(), 2);
                var eurRate = Math.Round(valute.GetProperty("EUR").GetProperty("Value").GetDouble(), 2);
                var cnyRate = Math.Round(valute.GetProperty("CNY").GetProperty("Value").GetDouble(), 2);
                
                return $"💵 Курсы ЦБ РФ (реальные):\n" +
                       $"USD → {usdRate}₽\n" +
                       $"EUR → {eurRate}₽\n" +
                       $"CNY → {cnyRate}₽";
            }
            catch (Exception ex)
            {
                return $"❌ Не удалось получить курсы валют\nОшибка API: {ex.Message}";
            }
        }

        private string GetHelp()
        {
            return @"📚 Доступные команды:
🌤️ /погода [город] - Погода
🧮 /calc выражение - Научный калькулятор
😂 /шутка - Случайная шутка
🕐 /время [город] - Время (Москва, Лондон, Нью-Йорк, Токио)
💵 /курс - Реальные курсы ЦБ РФ
❓ /помощь - Справка

🧮 Научный калькулятор:
• Основные: +, -, *, /, ^ (степень)
• Тригонометрия: sin(), cos(), tan()
• Логарифмы: log() (10), ln() (e)
• Корень: sqrt()
• Константы: pi, e
Примеры:
/calc 2+3*4
/calc sin(pi/2)
/calc log(100) + sqrt(16)
/calc 2^3 + cos(0)";
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
}
