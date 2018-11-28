using System;
using System.Diagnostics;

namespace Altairis.AutoAcme.Core {
    public static class Log {
        private static int indent = 0;
        private static bool isNewLine = true;

        public static bool VerboseMode;

        public static void Indent() { indent++; }

        public static void Unindent() {
            if (indent > 0)
                indent--;
        }

        public static void Exception(Exception ex, string message) {
            AssertNewLine();
            Write(message);
            if (ex is AggregateException aex) {
                WriteLine("!");
                foreach (var iaex in aex.Flatten().InnerExceptions) {
                    WriteLine(iaex.Message);
                    WriteVerboseLine();
                    WriteVerboseLine(iaex.ToString());
                }
            } else {
                Write(": ");
                WriteLine(ex.Message);
                WriteVerboseLine();
                WriteVerboseLine(ex.ToString());
            }
        }

        public static void AssertNewLine() {
            if (!isNewLine) {
                WriteLine();
            }
        }

        public static void Write(string value) {
            if (!String.IsNullOrEmpty(value)) {
                if (isNewLine) {
                    if (indent > 0) {
                        Trace.Write(new string(' ', indent * 2));
                    }
                    isNewLine = false;
                }
                Trace.Write(value);
            }
        }

        public static void WriteLine(string value = null) {
            if (value != null) {
                Write(value);
            }
            Trace.WriteLine(String.Empty);
            isNewLine = true;
        }

        public static void WriteVerboseLine(string value = null) {
            if (VerboseMode) {
                AssertNewLine();
                WriteLine(value);
            }
        }
    }
}
