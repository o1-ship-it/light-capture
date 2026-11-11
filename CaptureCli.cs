using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuickCapture
{
    internal static class CaptureCli
    {
        private const string Usage = "LightCapture.Cli capture --output <file.png> [--region <x> <y> <width> <height>]\n"
            + "LightCapture.Cli mcp";

        [STAThread]
        private static int Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
            {
                Console.WriteLine(Usage);
                return 0;
            }

            Native.EnableDpiAwareness();
            try
            {
                if (args.Length == 1 && args[0] == "mcp")
                    new ScreenshotMcp().Run();
                else
                    Save(args);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        private static void Save(string[] args)
        {
            if ((args.Length != 3 && args.Length != 8) || args[0] != "capture" ||
                args[1] != "--output" || (args.Length == 8 && args[3] != "--region"))
                throw new ArgumentException(Usage);

            string output = Path.GetFullPath(args[2]);
            if (!String.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output must be a .png file.");

            Rectangle bounds = args.Length == 3
                ? PrimaryBounds()
                : Region(ParseCoordinate(args[4]), ParseCoordinate(args[5]),
                         ParseCoordinate(args[6]), ParseCoordinate(args[7]));

            using (Bitmap image = ScreenCapture.Take(bounds))
            {
                var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                try
                {
                    using (stream) image.Save(stream, ImageFormat.Png);
                }
                catch
                {
                    try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    throw;
                }
            }
            Console.WriteLine(output);
        }

        private static int ParseCoordinate(string text)
        {
            int value;
            if (!Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException("Region coordinates and dimensions must be integers.");
            return value;
        }

        internal static Rectangle PrimaryBounds()
        {
            Screen primary = Screen.PrimaryScreen;
            if (primary == null)
                throw new InvalidOperationException("No primary display is available.");
            return primary.Bounds;
        }

        internal static Rectangle Region(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Region width and height must be positive.");

            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle bounds = screen.Bounds;
                if (x >= bounds.Left && y >= bounds.Top &&
                    (long)x + width <= bounds.Right && (long)y + height <= bounds.Bottom)
                    return new Rectangle(x, y, width, height);
            }
            throw new ArgumentException("Region must be entirely within one display.");
        }
    }

    internal sealed class ScreenshotMcp
    {
        private const string ToolName = "capture_screen";
        private const string ProtocolVersion = "2025-11-25";
        private readonly JavaScriptSerializer json = new JavaScriptSerializer
        {
            MaxJsonLength = 12 * 1024 * 1024,
            RecursionLimit = 24
        };
        private bool initialized;

        internal void Run()
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                object reply = Handle(line);
                if (reply != null) Console.WriteLine(json.Serialize(reply));
            }
        }

        private object Handle(string line)
        {
            Dictionary<string, object> message;
            try { message = json.DeserializeObject(line) as Dictionary<string, object>; }
            catch (ArgumentException) { return Error(null, -32700, "Parse error"); }
            catch (InvalidOperationException) { return Error(null, -32700, "Parse error"); }

            object id = Field(message, "id");
            string method = Field(message, "method") as string;
            if (!Object.Equals(Field(message, "jsonrpc"), "2.0") || method == null)
                return Error(null, -32600, "Invalid request");
            if (!message.ContainsKey("id"))
            {
                if (method == "notifications/initialized") initialized = true;
                return null;
            }

            var parameters = Field(message, "params") as Dictionary<string, object>;
            if (method == "initialize")
            {
                string version = Field(parameters, "protocolVersion") as string;
                if (String.IsNullOrEmpty(version)) return Error(id, -32602, "Missing protocolVersion");
                if (version != "2024-11-05" && version != "2025-03-26" && version != "2025-06-18")
                    version = ProtocolVersion;
                return Result(id, new
                {
                    protocolVersion = version,
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "light-capture", version = "1.0.0" },
                    instructions = "Use capture_screen for a one-time screenshot when the user requests it. This server does not control the mouse, keyboard, or windows. Prefer a region if the user provides coordinates."
                });
            }
            if (method == "ping") return Result(id, new { });
            if (!initialized) return Error(id, -32002, "Server not initialized");
            if (method == "tools/list") return Result(id, new { tools = new[] { CaptureTool() } });
            if (method != "tools/call") return Error(id, -32601, "Method not found");
            if (!Object.Equals(Field(parameters, "name"), ToolName))
                return Error(id, -32602, "Unknown tool");

            try
            {
                return Result(id, Capture(Field(parameters, "arguments")));
            }
            catch (Exception error)
            {
                return Result(id, new { isError = true, content = new[] { new { type = "text", text = error.Message } } });
            }
        }

        private static object CaptureTool()
        {
            var integer = new { type = "integer" };
            var dimension = new { type = "integer", minimum = 1 };
            return new
            {
                name = ToolName,
                description = "Capture the currently visible pixels of the primary display, or one explicit region within a display. Returns a PNG. Does not focus windows or send mouse or keyboard input.",
                annotations = new { readOnlyHint = true, destructiveHint = false, openWorldHint = false },
                inputSchema = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new
                    {
                        region = new
                        {
                            type = "object",
                            description = "Physical pixels in Windows virtual desktop coordinates. The primary display starts at (0, 0).",
                            properties = new { x = integer, y = integer, width = dimension, height = dimension },
                            required = new[] { "x", "y", "width", "height" },
                            additionalProperties = false
                        }
                    }
                }
            };
        }

        private static object Capture(object rawArguments)
        {
            var arguments = rawArguments as Dictionary<string, object>;
            if (rawArguments != null && arguments == null)
                throw new ArgumentException("Arguments must be an object.");

            Rectangle bounds;
            if (arguments == null || arguments.Count == 0)
            {
                bounds = CaptureCli.PrimaryBounds();
            }
            else
            {
                var region = Field(arguments, "region") as Dictionary<string, object>;
                if (arguments.Count != 1 || region == null || region.Count != 4)
                    throw new ArgumentException("Specify a region with x, y, width and height.");
                bounds = CaptureCli.Region(Coordinate(region, "x"), Coordinate(region, "y"),
                                           Coordinate(region, "width"), Coordinate(region, "height"));
            }
            using (Bitmap image = ScreenCapture.Take(bounds))
            using (var png = new MemoryStream())
            {
                image.Save(png, ImageFormat.Png);
                if (png.Length > 8 * 1024 * 1024)
                    throw new InvalidOperationException("Screenshot exceeds 8 MiB. Request a smaller region.");
                string data = Convert.ToBase64String(png.GetBuffer(), 0, (int)png.Length);
                return new { content = new[] { new { type = "image", mimeType = "image/png", data = data } } };
            }
        }

        private static int Coordinate(Dictionary<string, object> region, string key)
        {
            object value = Field(region, key);
            if (!(value is int)) throw new ArgumentException("Region " + key + " must be a 32-bit integer.");
            return (int)value;
        }

        private static object Field(Dictionary<string, object> fields, string key)
        {
            object value;
            return fields != null && fields.TryGetValue(key, out value) ? value : null;
        }

        private static object Result(object id, object result)
        {
            return new { jsonrpc = "2.0", id = id, result = result };
        }

        private static object Error(object id, int code, string message)
        {
            return new { jsonrpc = "2.0", id = id, error = new { code = code, message = message } };
        }
    }
}
