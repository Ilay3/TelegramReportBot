using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// ID топиков в Telegram-группе
    /// </summary>
    public class TopicIds
    {
        /// <summary>
        /// Топик "Предупреждения" (topic_id = 11)
        /// </summary>
        public int Warnings { get; set; }

        /// <summary>
        /// Топик "Пользовательские ошибки" (topic_id = 9)
        /// </summary>
        public int UserErrors { get; set; }

        /// <summary>
        /// Топик "Серверные ошибки" (topic_id = 7)
        /// </summary>
        public int ServerErrors { get; set; }
    }
}
