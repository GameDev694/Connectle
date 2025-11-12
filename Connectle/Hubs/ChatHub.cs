using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Connectle.Hubs
{
    public class ChatHub : Hub
    {
        // === СТАТИЧЕСКИЕ ДАННЫЕ ===
        private static List<Message> _messages = new();
        private static List<User> _users = new();
        private static List<PrivateMessage> _privateMessages = new();
        private static List<Contact> _contacts = new();
        
        // === БЛОКИРОВКИ ДЛЯ ПОТОКОБЕЗОПАСНОСТИ ===
        private static readonly object _messagesLock = new();
        private static readonly object _usersLock = new();
        private static readonly object _privateMessagesLock = new();
        private static readonly object _contactsLock = new();

        // === ОБЩИЕ СООБЩЕНИЯ ===
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("ReceiveMessageHistory", GetMessages());
            await base.OnConnectedAsync();
        }

        public async Task SendMessage(string user, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Проверка аутентификации
            var currentUser = GetUserByConnectionId(Context.ConnectionId);
            if (currentUser == null)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", 
                    "❌ Для отправки сообщений необходимо войти в систему", DateTime.Now);
                return;
            }

            if (text.StartsWith("/"))
            {
                var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 0)
                {
                    var result = await ExecutePluginCommand(args[0].ToLower(), args);
                    await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", result, DateTime.Now);
                }
                return;
            }

            // Ограничение длины сообщения
            if (text.Length > 1000)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", 
                    "❌ Сообщение слишком длинное (максимум 1000 символов)", DateTime.Now);
                return;
            }

            var message = new Message(currentUser.Username, text, DateTime.Now);
            
            lock (_messagesLock)
            {
                _messages.Add(message);
                // Ограничение истории сообщений
                if (_messages.Count > 1000)
                    _messages.RemoveAt(0);
            }

            await Clients.All.SendAsync("ReceiveMessage", message.User, message.Text, message.Timestamp);
        }

        // === АУТЕНТИФИКАЦИЯ ===
        public async Task<AuthResult> Register(string username, string email, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
                return new AuthResult { Success = false, Message = "Имя пользователя должно быть не менее 3 символов" };

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                return new AuthResult { Success = false, Message = "Пароль должен быть не менее 6 символов" };

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return new AuthResult { Success = false, Message = "Некорректный email" };

            lock (_usersLock)
            {
                if (_users.Any(u => u.Username == username))
                    return new AuthResult { Success = false, Message = "Имя пользователя уже занято" };

                if (_users.Any(u => u.Email == email))
                    return new AuthResult { Success = false, Message = "Email уже используется" };

                var user = new User 
                { 
                    Id = Guid.NewGuid(),
                    Username = username, 
                    Email = email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    CreatedAt = DateTime.Now
                };
                
                _users.Add(user);
                return new AuthResult { Success = true, Message = "Регистрация успешна", User = user };
            }
        }

        public async Task<AuthResult> Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return new AuthResult { Success = false, Message = "Заполните все поля" };

            User user;
            lock (_usersLock)
            {
                user = _users.FirstOrDefault(u => u.Username == username);
            }

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return new AuthResult { Success = false, Message = "Неверный логин или пароль" };

            lock (_usersLock)
            {
                user.IsOnline = true;
                user.ConnectionId = Context.ConnectionId;
                user.LastSeen = DateTime.Now;
            }

            await Clients.Caller.SendAsync("LoginSuccess", user);
            await UpdateOnlineStatus();
            
            // Отправляем историю сообщений после входа
            await Clients.Caller.SendAsync("ReceiveMessageHistory", GetMessages());
            
            return new AuthResult { Success = true, Message = "Вход успешен", User = user };
        }

        public async Task Logout()
        {
            var user = GetUserByConnectionId(Context.ConnectionId);
            if (user != null)
            {
                lock (_usersLock)
                {
                    user.IsOnline = false;
                    user.ConnectionId = null;
                    user.LastSeen = DateTime.Now;
                }
                await UpdateOnlineStatus();
            }
        }

        // === ЛИЧНЫЕ СООБЩЕНИЯ ===
        public async Task SendPrivateMessage(string toUsername, string text)
        {
            var fromUser = GetUserByConnectionId(Context.ConnectionId);
            if (fromUser == null)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", 
                    "❌ Для отправки сообщений необходимо войти в систему", DateTime.Now);
                return;
            }

            if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
            {
                await Clients.Caller.SendAsync("ReceivePrivateMessage", new
                {
                    FromUser = "🤖 Система",
                    ToUser = fromUser.Username,
                    Text = "❌ Сообщение слишком длинное или пустое",
                    Timestamp = DateTime.Now,
                    IsOwn = true
                });
                return;
            }

            User toUser;
            lock (_usersLock)
            {
                toUser = _users.FirstOrDefault(u => u.Username == toUsername);
            }

            if (toUser == null)
            {
                await Clients.Caller.SendAsync("ReceivePrivateMessage", new
                {
                    FromUser = "🤖 Система",
                    ToUser = fromUser.Username,
                    Text = "❌ Пользователь не найден",
                    Timestamp = DateTime.Now,
                    IsOwn = true
                });
                return;
            }

            if (fromUser.Id == toUser.Id)
            {
                await Clients.Caller.SendAsync("ReceivePrivateMessage", new
                {
                    FromUser = "🤖 Система",
                    ToUser = fromUser.Username,
                    Text = "❌ Нельзя отправлять сообщения самому себе",
                    Timestamp = DateTime.Now,
                    IsOwn = true
                });
                return;
            }

            var message = new PrivateMessage
            {
                Id = Guid.NewGuid(),
                FromUserId = fromUser.Id,
                ToUserId = toUser.Id,
                Text = text,
                Timestamp = DateTime.Now,
                IsRead = false
            };

            lock (_privateMessagesLock)
            {
                _privateMessages.Add(message);
            }

            // Отправляем отправителю
            await Clients.Caller.SendAsync("ReceivePrivateMessage", new
            {
                FromUser = fromUser.Username,
                ToUser = toUser.Username,
                Text = text,
                Timestamp = message.Timestamp,
                IsOwn = true
            });

            // Отправляем получателю, если онлайн
            if (toUser.IsOnline && !string.IsNullOrEmpty(toUser.ConnectionId))
            {
                await Clients.Client(toUser.ConnectionId).SendAsync("ReceivePrivateMessage", new
                {
                    FromUser = fromUser.Username,
                    ToUser = toUser.Username,
                    Text = text,
                    Timestamp = message.Timestamp,
                    IsOwn = false
                });
            }
        }

        public async Task<List<PrivateMessage>> GetPrivateMessageHistory(string withUsername)
        {
            var currentUser = GetUserByConnectionId(Context.ConnectionId);
            if (currentUser == null) return new List<PrivateMessage>();

            User withUser;
            lock (_usersLock)
            {
                withUser = _users.FirstOrDefault(u => u.Username == withUsername);
            }

            if (withUser == null) return new List<PrivateMessage>();

            lock (_privateMessagesLock)
            {
                return _privateMessages
                    .Where(m => (m.FromUserId == currentUser.Id && m.ToUserId == withUser.Id) ||
                               (m.FromUserId == withUser.Id && m.ToUserId == currentUser.Id))
                    .OrderBy(m => m.Timestamp)
                    .Take(100) // Ограничение истории
                    .ToList();
            }
        }

        // === КОНТАКТЫ ===
        public async Task AddContact(string username)
        {
            var currentUser = GetUserByConnectionId(Context.ConnectionId);
            if (currentUser == null) return;

            User contactUser;
            lock (_usersLock)
            {
                contactUser = _users.FirstOrDefault(u => u.Username == username);
            }

            if (contactUser == null)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", 
                    "❌ Пользователь не найден", DateTime.Now);
                return;
            }

            if (currentUser.Id == contactUser.Id)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "🤖 Система", 
                    "❌ Нельзя добавить себя в контакты", DateTime.Now);
                return;
            }

            lock (_contactsLock)
            {
                if (_contacts.Any(c => c.UserId == currentUser.Id && c.ContactUserId == contactUser.Id))
                {
                    return;
                }

                var contact = new Contact
                {
                    Id = Guid.NewGuid(),
                    UserId = currentUser.Id,
                    ContactUserId = contactUser.Id,
                    DisplayName = username,
                    AddedAt = DateTime.Now
                };

                _contacts.Add(contact);
            }

            await Clients.Caller.SendAsync("ContactAdded", new
            {
                Username = username,
                IsOnline = contactUser.IsOnline
            });
        }

        public async Task<List<ContactInfo>> GetContacts()
        {
            var currentUser = GetUserByConnectionId(Context.ConnectionId);
            if (currentUser == null) return new List<ContactInfo>();

            List<ContactInfo> userContacts;
            
            lock (_contactsLock)
            lock (_usersLock)
            {
                userContacts = _contacts
                    .Where(c => c.UserId == currentUser.Id)
                    .Select(c => new ContactInfo
                    {
                        Username = _users.First(u => u.Id == c.ContactUserId).Username,
                        IsOnline = _users.First(u => u.Id == c.ContactUserId).IsOnline,
                        LastSeen = _users.First(u => u.Id == c.ContactUserId).LastSeen
                    })
                    .ToList();
            }

            return userContacts;
        }

        // === ОНЛАЙН СТАТУС ===
        private async Task UpdateOnlineStatus()
        {
            List<string> onlineUsers;
            lock (_usersLock)
            {
                onlineUsers = _users
                    .Where(u => u.IsOnline)
                    .Select(u => u.Username)
                    .ToList();
            }
                
            await Clients.All.SendAsync("OnlineUsersUpdated", onlineUsers);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var user = GetUserByConnectionId(Context.ConnectionId);
            if (user != null)
            {
                lock (_usersLock)
                {
                    user.IsOnline = false;
                    user.ConnectionId = null;
                    user.LastSeen = DateTime.Now;
                }
                await UpdateOnlineStatus();
            }
            await base.OnDisconnectedAsync(exception);
        }

        // === PLUGIN КОМАНДЫ (из первого кода) ===
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
                    "/контакты" => await GetContactsCommand(),
                    "/онлайн" => GetOnlineUsers(),
                    _ => "❌ Неизвестная команда. Напишите /помощь"
                };
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка: {ex.Message}";
            }
        }

        private async Task<string> GetContactsCommand()
        {
            var contacts = await GetContacts();
            if (!contacts.Any())
                return "📇 У вас нет контактов. Добавьте их командой /добавить [имя]";

            return "📇 Ваши контакты:\n" + string.Join("\n", 
                contacts.Select(c => $"{c.Username} {(c.IsOnline ? "🟢" : "⚫")}"));
        }

        private string GetOnlineUsers()
        {
            List<string> onlineUsers;
            lock (_usersLock)
            {
                onlineUsers = _users
                    .Where(u => u.IsOnline)
                    .Select(u => u.Username)
                    .ToList();
            }

            if (!onlineUsers.Any())
                return "👥 В сети никого нет";

            return "👥 Пользователи онлайн:\n" + string.Join("\n", onlineUsers);
        }

        // === ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ===
        private User GetUserByConnectionId(string connectionId)
        {
            lock (_usersLock)
            {
                return _users.FirstOrDefault(u => u.ConnectionId == connectionId);
            }
        }

        private List<Message> GetMessages()
        {
            lock (_messagesLock)
            {
                return _messages.ToList();
            }
        }

        // === МЕТОДЫ ПЛАГИНОВ (из первого кода) ===
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

🌐 ОБЩИЕ:
🌤️ /погода [город] - Погода
🧮 /calc выражение - Научный калькулятор
😂 /шутка - Случайная шутка
🕐 /время [город] - Время (Москва, Лондон, Нью-Йорк, Токио)
💵 /курс - Реальные курсы ЦБ РФ

👥 СОЦИАЛЬНЫЕ:
/контакты - Мои контакты
/онлайн - Кто онлайн
/добавить [имя] - Добавить в контакты
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

        // === МОДЕЛИ ДАННЫХ ===
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

        public class User
        {
            public Guid Id { get; set; }
            public string Username { get; set; }
            public string Email { get; set; }
            public string PasswordHash { get; set; }
            public string ConnectionId { get; set; }
            public bool IsOnline { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastSeen { get; set; }
        }

        public class PrivateMessage
        {
            public Guid Id { get; set; }
            public Guid FromUserId { get; set; }
            public Guid ToUserId { get; set; }
            public string Text { get; set; }
            public DateTime Timestamp { get; set; }
            public bool IsRead { get; set; }
        }

        public class Contact
        {
            public Guid Id { get; set; }
            public Guid UserId { get; set; }
            public Guid ContactUserId { get; set; }
            public string DisplayName { get; set; }
            public DateTime AddedAt { get; set; }
        }

        public class AuthResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public User User { get; set; }
        }

        public class ContactInfo
        {
            public string Username { get; set; }
            public bool IsOnline { get; set; }
            public DateTime LastSeen { get; set; }
        }
    }
}
