using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models.Enum
{
    /// <summary>
    /// Административные команды
    /// </summary>
    public enum AdminCommand
    {
        ClearSentFiles,
        ForceBackup,
        ReloadConfig,
        RestartBot,
        ViewLogs,
        ExportStatistics,
        TestConnection,
        CleanupFiles,
        UpdateSettings
    }

}
