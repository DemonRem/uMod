﻿using System;
using System.Linq;

namespace uMod.ServerConsole
{
    public class ServerConsole
    {
        private readonly ConsoleWindow console = new ConsoleWindow();
        private readonly ConsoleInput input = new ConsoleInput();
        private bool init;
        private float nextUpdate;
        private float nextTitleUpdate;

        public event Action<string> Input;

        public Func<string> Title;
        public Func<string> Status1Left;
        public Func<string> Status1Right;
        public Func<string> Status2Left;
        public Func<string> Status2Right;
        public Func<string> Status3Left;
        public Func<string> Status3Right;

        public Func<string, string[]> Completion
        {
            get => input.Completion;
            set => input.Completion = value;
        }

        public ConsoleColor Status1LeftColor
        {
            get => input.StatusTextLeftColor[1];
            set => input.StatusTextLeftColor[1] = value;
        }

        public ConsoleColor Status1RightColor
        {
            get => input.StatusTextRightColor[1];
            set => input.StatusTextRightColor[1] = value;
        }

        public ConsoleColor Status2LeftColor
        {
            get => input.StatusTextLeftColor[2];
            set => input.StatusTextLeftColor[2] = value;
        }

        public ConsoleColor Status2RightColor
        {
            get => input.StatusTextRightColor[2];
            set => input.StatusTextRightColor[2] = value;
        }

        public ConsoleColor Status3RightColor
        {
            get => input.StatusTextRightColor[3];
            set => input.StatusTextRightColor[3] = value;
        }

        public ConsoleColor Status3LeftColor
        {
            get => input.StatusTextLeftColor[3];
            set => input.StatusTextLeftColor[3] = value;
        }

        private string title => Title?.Invoke();
        private string status1Left => GetStatusValue(Status1Left);
        private string status1Right => GetStatusValue(Status1Right).PadLeft(input.LineWidth - 1);
        private string status2Left => GetStatusValue(Status2Left);
        private string status2Right => GetStatusValue(Status2Right).PadLeft(input.LineWidth - 1);
        private string status3Left => GetStatusValue(Status3Left);
        private string status3Right => GetStatusValue(Status3Right).PadLeft(input.LineWidth - 1);

        private static string GetStatusValue(Func<string> status) => status != null ? status() ?? string.Empty : "";

        private static string GetStatusRight(int leftLength, string right) => leftLength >= right.Length ? string.Empty : right.Substring(leftLength);

        public void AddMessage(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            Console.ForegroundColor = color;
            int messageLength = message.Split('\n').Aggregate(0, (sum, line) => sum + (int)Math.Ceiling((double)line.Length / Console.BufferWidth));
            input.ClearLine((Interface.uMod.Config.Console.ShowStatusBar ? input.StatusTextLeft.Length : 0) + messageLength);
            Console.WriteLine(message);
            input.RedrawInputLine();
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void OnDisable()
        {
            if (init)
            {
                input.OnInputText -= OnInputText;
                console.Shutdown();
            }
        }

        public void OnEnable()
        {
            if (console.Initialize())
            {
                init = true;
                input.OnInputText += OnInputText;
                input.ClearLine(1);
                input.ClearLine(Console.WindowHeight);
                for (int i = 0; i < Console.WindowHeight; i++)
                {
                    Console.WriteLine();
                }
            }
        }

        private void OnInputText(string obj)
        {
            try
            {
                Input?.Invoke(obj);
            }
            catch (Exception e)
            {
                Interface.uMod.LogException("OnInputText: ", e);
            }
        }

        public static void PrintColored(params object[] objects)
        {
            if (Interface.uMod.ServerConsole == null)
            {
                return;
            }

            Interface.uMod.ServerConsole.input.ClearLine(Interface.uMod.Config.Console.ShowStatusBar ? Interface.uMod.ServerConsole.input.StatusTextLeft.Length : 1);
            for (int i = 0; i < objects.Length; i++)
            {
                if (i % 2 != 0)
                {
                    Console.Write((string)objects[i]);
                }
                else
                {
                    Console.ForegroundColor = (ConsoleColor)(int)objects[i];
                }
            }
            if (Console.CursorLeft != 0)
            {
                Console.CursorTop = Console.CursorTop + 1;
            }

            Interface.uMod.ServerConsole.input.RedrawInputLine();
        }

        public void Update()
        {
            if (init)
            {
                if (Interface.uMod.Config.Console.ShowStatusBar)
                {
                    UpdateStatus();
                }

                input.Update();
                if (nextTitleUpdate > Interface.uMod.Now)
                {
                    return;
                }

                nextTitleUpdate = Interface.uMod.Now + 1f;
                console.SetTitle(title);
            }
        }

        private void UpdateStatus()
        {
            if (!(nextUpdate > Interface.uMod.Now))
            {
                nextUpdate = Interface.uMod.Now + 0.66f;
                if (input.Valid)
                {
                    string left1 = status1Left, left2 = status2Left, left3 = status3Left;

                    //input.StatusTextLeft[0] = string.Empty;
                    input.StatusTextLeft[1] = left1;
                    input.StatusTextLeft[2] = left2;
                    input.StatusTextLeft[3] = left3;

                    //input.StatusTextRight[0] = string.Empty;
                    input.StatusTextRight[1] = GetStatusRight(left1.Length, status1Right);
                    input.StatusTextRight[2] = GetStatusRight(left2.Length, status2Right);
                    input.StatusTextRight[3] = GetStatusRight(left3.Length, status3Right);
                }
            }
        }
    }
}
