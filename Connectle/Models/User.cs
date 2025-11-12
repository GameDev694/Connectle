namespace Connectle.Models
{
    public class User
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsOnline { get; set; }
        public string ConnectionId { get; set; }
    }

    public class Contact
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }  // Владелец контакта
        public string ContactUserId { get; set; }  // Кто в контактах
        public string DisplayName { get; set; }
    }

    public class PrivateMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FromUserId { get; set; }
        public string ToUserId { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsRead { get; set; }
    }

    public class ChatRoom
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public List<string> MemberIds { get; set; } = new();
        public bool IsGroup { get; set; }
    }
}
