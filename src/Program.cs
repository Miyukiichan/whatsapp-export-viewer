using System.Text.RegularExpressions;
using CommandLine;
using HtmlAgilityPack;

var options = Parser.Default.ParseArguments<Options>(args).Value;
if (options is null) return;
var mediaDir = Path.Combine(options.Directory, "media");
var chatFile = Path.Combine(options.Directory, "chat.txt");

if (!Directory.Exists(options.Directory))
    throw new Exception($"Directory {options.Directory} does not exist");

if (!Directory.Exists(mediaDir))
    throw new Exception($"Directory {mediaDir} does not exist");

if (!File.Exists(chatFile))
    throw new Exception($"Directory {chatFile} does not exist");

var chat = new Dictionary<string, List<Message>>();
var lines = File.ReadAllLines(chatFile);
string msgPattern = @"^[0-9]{2}\/[0-9]{2}\/[0-9]{4}, [0-9]{2}:[0-9]{2} - ";
Regex regex = new Regex(msgPattern);

// Put reference to message here so we can append in the case of line breaks in message body
Message? message = null;

foreach (var line in lines) {
    var matches = regex.Matches(line);
    if (!matches.Any()) {
        if (message is null) 
            throw new Exception("Error reading file - line break without a message");
        message.Lines.Add(line);
        continue;
    }
    //{Date}, {Time} - {Name}: {Message}
    var dateStr = line.Substring(0, 10);
    var date = DateOnly.Parse(dateStr);
    var timeStr = line.Substring(12, 5);
    var time = TimeOnly.Parse(timeStr);
    var name = string.Empty;
    var body = string.Empty;
    var nameIdx = 21;
    // The time colon is always present at index 14
    var nameEndIdx = line.IndexOf(":", 15);
    if (nameEndIdx > -1) {
        name = line.Substring(nameIdx - 1, nameEndIdx - nameIdx + 1);
        var bodyIdx = nameEndIdx + 2;
        body = line.Substring(bodyIdx, line.Length - bodyIdx);
    }
    else {
        body = line.Substring(nameIdx - 1, line.Length - nameIdx);
    }
    // Sometimes whatsapp just puts random null chats that don't exist in the app
    if (body.ToLower().Equals("null")) continue;
    message = new Message {
        Date = date,
        Time = time,
        Name = name,
        Lines = new List<string> { body },
    };
    List<Message>? messages;
    if (!chat.TryGetValue(dateStr, out messages)) {
        messages = new List<Message>();
        chat.Add(dateStr, messages);
    }
    messages.Add(message);
}

// Check media exists

foreach (var day in chat) {
    var mediaMessages = day.Value.Where(x => x.Lines.First().ToLower().Equals("<media omitted>")).ToList();
    for (var i = 0; i < mediaMessages.Count(); i++) {
        var date = DateTime.Parse(day.Key).ToString("yyyyMMdd");
        var filename = $"{date}-WA{i.ToString().PadLeft(i.ToString().Length + 3, '0')}";
        var mediaPath = Path.Combine(mediaDir, filename);
        var jpegPath = $"{mediaPath}.jpeg";
        var jpgPath = $"{mediaPath}.jpg";
        if (File.Exists(jpegPath)) {
            mediaMessages[i].ImagePath = jpegPath;
        }
        else if (File.Exists(jpgPath)) {
            mediaMessages[i].ImagePath = jpgPath;
        }
        else {
            var videoPath = $"{mediaPath}.mp4";
            if (File.Exists(videoPath)) { 
                mediaMessages[i].VideoPath = videoPath;
            }
            else {
                throw new Exception($"Cannot find media file {mediaPath}");
            }
        }
    }
}

var htmlDoc = new HtmlDocument();
htmlDoc.Load("template.html");
var chatName = Path.GetFileName(options.Directory);
var title = $"Whatsapp Chat - {chatName}";
htmlDoc.DocumentNode.SelectSingleNode("//head").AppendChild(HtmlNode.CreateNode($"<title>{title}</title>"));
var htmlBody = htmlDoc.DocumentNode.SelectSingleNode("//body");
htmlBody.AppendChild(HtmlNode.CreateNode($"<h1>{title}</h1>"));
foreach(var day in chat) {
    var dateDiv = htmlBody.AppendChild(HtmlNode.CreateNode("<div class='date'>"));
    dateDiv.AppendChild(HtmlNode.CreateNode($"<p class='date-text'>{day.Key}</p>"));
    foreach (var msg in day.Value) {
        var className = msg.Name == options.User ? "message-sent" : "message-received"; 
        var div = htmlBody.AppendChild(HtmlNode.CreateNode($"<div class='message {className}'>"));
        div.AppendChild(HtmlNode.CreateNode($"<p class='name'><b>{msg.Name}</b></p>"));
        if (!string.IsNullOrWhiteSpace(msg.ImagePath)) {
            div.AppendChild(HtmlNode.CreateNode($"<img src='{msg.ImagePath}' />"));
        }
        else if (!string.IsNullOrWhiteSpace(msg.VideoPath)) {
            div.AppendChild(HtmlNode.CreateNode($"<video controls><source src='{msg.VideoPath}' /></video>"));
        }
        else {
            foreach (var line in msg.Lines) {
                if (string.IsNullOrWhiteSpace(line)) {
                div.AppendChild(HtmlNode.CreateNode($"<br />"));
                }
                var text = processUrl(line);
                div.AppendChild(HtmlNode.CreateNode($"<p>{text}</p>"));
            }
        }
        div.AppendChild(HtmlNode.CreateNode($"<p class='time'>{msg.Time.ToShortTimeString()}</p>"));
    }
}

htmlDoc.Save(options.Output);

//https://stackoverflow.com/a/4750468
string processUrl(string text) {
    return Regex.Replace(text,
        @"((http|ftp|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)",
        "<a target='_blank' href='$1'>$1</a>");
}

class Message {
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public string Name { get; set; }
    public List<string> Lines { get; set; }
    public string ImagePath { get; set; }
    public string VideoPath { get; set; }
}

public class Options
{
    [Option('d', "directory", Required = true, HelpText = "Directory containing chat and media")]
    public string Directory { get; set; }
    [Option('o', "output", Required = true, HelpText = "Output html file")]
    public string Output { get; set; }
    [Option('u', "user", Required = true, HelpText = "The name of the user from which the extract should be performed - ie who is the sender")]
    public string User { get; set; }
}
