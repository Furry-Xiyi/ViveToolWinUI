using System;
using System.Collections.Generic;
using System.Globalization;

namespace ViveToolWinUI
{
    /// <summary>
    /// 本地化字典辅助类 - 不依赖 ResX 文件，直接用硬编码的字典
    /// </summary>
    public static class LocalizationHelper
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
        {
            // 中文
            ["zh-CN"] = new()
            {
                // 对话框
                ["ClearHistoryDialog.Title"] = "确认删除",
                ["ClearHistoryDialog.Content"] = "是否清空最近操作列表？",
                ["ClearHistoryDialog.PrimaryButtonText"] = "删除",
                ["ClearHistoryDialog.CloseButtonText"] = "取消",

                // 右键菜单
                ["HistoryMenu.Enable.Text"] = "启用此 ID",
                ["HistoryMenu.Disable.Text"] = "禁用此 ID",
                ["HistoryMenu.Reset.Text"] = "恢复默认",
                ["HistoryMenu.Delete.Text"] = "删除此记录",

                // 输入对话框
                ["AddSubTitle"] = "addsub 参数",
                ["AddSubMessage"] = "请输入 addsub 参数（例如: /id:12345 /user:...）",
                ["DelSubTitle"] = "delsub 参数",
                ["DelSubMessage"] = "请输入 delsub 参数（例如: /id:12345 /user:...）",
                ["NotifyUsageTitle"] = "notifyusage 参数",
                ["NotifyUsageMessage"] = "请输入 notifyusage 参数（例如: /id:12345 /usage:1）",
                ["ExportTitle"] = "export 参数",
                ["ExportMessage"] = "可选：输出文件路径或留空（将在当前工作目录生成）",
                ["ImportTitle"] = "import 参数",
                ["ImportMessage"] = "请输入要导入的文件路径（必填）",

                // 通用按钮
                ["DialogOK"] = "确定",
                ["DialogCancel"] = "取消",

                // 重启对话框
                ["RebootTitle"] = "重启系统",
                ["RebootMessage"] = "命令执行完成。是否立即重启系统以应用更改？",
                ["RebootNow"] = "立即重启",
                ["RebootLater"] = "稍后手动重启",

                // 错误消息
                ["WarningEmptyFeatureId"] = "请输入 Feature ID",
                ["ErrorInvalidFeatureId"] = "Feature ID 格式无效",
                ["ErrorTooManyIds"] = "指定的 Feature ID 数量太多",
                ["ErrorIdLength"] = "单个 Feature ID 长度应为 8 位（例如 56848060）",
                ["WarningEmptyArgs"] = "请输入参数",
                ["ErrorViveToolNotFound"] = "未找到 ViveTool",
                ["ErrorUACDenied"] = "请求管理员权限被拒绝或无法获取",
                ["ErrorProcessStart"] = "无法启动提升的命令进程",
                ["ErrorTimeout"] = "ViveTool 执行超时",
                ["ErrorParseFeatureId"] = "ViveTool 返回解析错误或未指定 Feature ID，请检查输入。",
                ["ErrorPartialFailed"] = "部分 Feature 未按预期应用: {0}。请检查输出并重试。",
                ["SuccessExecution"] = "命令执行成功",
                ["ErrorExecutionFailed"] = "命令执行失败，详情见输出。",
                ["ErrorNoOutput"] = "未捕获到 vivetool 输出，已保留临时输出文件以便排查（查看日志）。",
                ["ErrorExecution"] = "执行失败: {0}",
                ["InfoRebootScheduled"] = "系统将在 10 秒后重启",
                ["ErrorRebootFailed"] = "无法重启系统: {0}",
                ["InfoRebootIncluded"] = "包含 /reboot 参数；系统将在命令执行后按 vivetool 的行为重启（若需要）。",
            },
            // 英文
            ["en-US"] = new()
            {
                // Dialog
                ["ClearHistoryDialog.Title"] = "Confirm Delete",
                ["ClearHistoryDialog.Content"] = "Clear recent actions list?",
                ["ClearHistoryDialog.PrimaryButtonText"] = "Delete",
                ["ClearHistoryDialog.CloseButtonText"] = "Cancel",

                // Context Menu
                ["HistoryMenu.Enable.Text"] = "Enable This ID",
                ["HistoryMenu.Disable.Text"] = "Disable This ID",
                ["HistoryMenu.Reset.Text"] = "Reset",
                ["HistoryMenu.Delete.Text"] = "Delete This Record",

                // Input Dialogs
                ["AddSubTitle"] = "addsub Parameters",
                ["AddSubMessage"] = "Enter addsub parameters (e.g.: /id:12345 /user:...)",
                ["DelSubTitle"] = "delsub Parameters",
                ["DelSubMessage"] = "Enter delsub parameters (e.g.: /id:12345 /user:...)",
                ["NotifyUsageTitle"] = "notifyusage Parameters",
                ["NotifyUsageMessage"] = "Enter notifyusage parameters (e.g.: /id:12345 /usage:1)",
                ["ExportTitle"] = "export Parameters",
                ["ExportMessage"] = "Optional: Output file path or leave empty (will generate in current directory)",
                ["ImportTitle"] = "import Parameters",
                ["ImportMessage"] = "Enter the file path to import (required)",

                // Common Buttons
                ["DialogOK"] = "OK",
                ["DialogCancel"] = "Cancel",

                // Reboot Dialog
                ["RebootTitle"] = "Restart System",
                ["RebootMessage"] = "Command executed successfully. Restart system now to apply changes?",
                ["RebootNow"] = "Restart Now",
                ["RebootLater"] = "Restart Later",

                // Error Messages
                ["WarningEmptyFeatureId"] = "Please enter Feature ID",
                ["ErrorInvalidFeatureId"] = "Invalid Feature ID format",
                ["ErrorTooManyIds"] = "Too many Feature IDs specified",
                ["ErrorIdLength"] = "Each Feature ID should be 8 digits (e.g. 56848060)",
                ["WarningEmptyArgs"] = "Please enter parameters",
                ["ErrorViveToolNotFound"] = "ViveTool not found",
                ["ErrorUACDenied"] = "Administrator privilege request denied or cannot be obtained",
                ["ErrorProcessStart"] = "Cannot start elevated command process",
                ["ErrorTimeout"] = "ViveTool execution timeout",
                ["ErrorParseFeatureId"] = "ViveTool returned parse error or no Feature ID specified. Please check your input.",
                ["ErrorPartialFailed"] = "Some Features were not applied as expected: {0}. Please check the output and retry.",
                ["SuccessExecution"] = "Command executed successfully",
                ["ErrorExecutionFailed"] = "Command execution failed. See output for details.",
                ["ErrorNoOutput"] = "No vivetool output captured. Temporary output files preserved for debugging (see logs).",
                ["ErrorExecution"] = "Execution failed: {0}",
                ["InfoRebootScheduled"] = "System will restart in 10 seconds",
                ["ErrorRebootFailed"] = "Cannot restart system: {0}",
                ["InfoRebootIncluded"] = "/reboot parameter included; system will restart after command execution as vivetool determines (if needed).",
            }
        };

        /// <summary>
        /// 获取本地化字符串
        /// </summary>
        public static string GetString(string key)
        {
            // 根据系统语言获取
            var culture = CultureInfo.CurrentUICulture.Name;

            // 尝试匹配精确的语言代码（如 zh-CN, en-US）
            if (Strings.TryGetValue(culture, out var dict) && dict.TryGetValue(key, out var value))
                return value;

            // 尝试匹配语言前缀（如 zh, en）
            var langPrefix = culture.Split('-')[0];
            foreach (var kvp in Strings)
            {
                if (kvp.Key.StartsWith(langPrefix) && kvp.Value.TryGetValue(key, out var fallbackValue))
                    return fallbackValue;
            }

            // 最后回退到中文
            if (Strings["zh-CN"].TryGetValue(key, out var zhValue))
                return zhValue;

            // 都找不到就返回键名本身
            return key;
        }
    }
}