using System.Text.RegularExpressions;

namespace FFXILauncher.Core;

public sealed record OcrWord(string Text, double X, double Y, double Width, double Height);
public sealed record OcrLine(string Text, OcrWord[] Words);
public sealed record LoginFrame(int Width, int Height, OcrLine[] Lines)
{
    public string Text => string.Join("\n", Lines.Select(l => l.Text));
    public bool Has(string text) => Normalize(Text).Contains(Normalize(text), StringComparison.Ordinal);
    public static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]", "");
    public OcrLine? UniqueLine(string text) => Lines.Where(l => Normalize(l.Text) == Normalize(text)).Take(2).ToArray() is [var line] ? line : null;
    public bool HasIdentity(string identity) => IdentityCandidates(identity).Length > 0;
    public OcrWord[] IdentityCandidates(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Any(char.IsWhiteSpace)) return [];
        var matches = new List<OcrWord>();
        static bool Token(OcrWord w) => Regex.IsMatch(w.Text, "^[A-Za-z0-9_.-]+$") && double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) && w.Width > 0 && w.Height > 0;
        static bool Adjacent(OcrWord a, OcrWord b) => b.X - a.X - a.Width >= 0 && b.X - a.X - a.Width <= Math.Max(a.Height, b.Height) * .6 && Math.Abs(b.Y - a.Y) <= Math.Max(a.Height, b.Height) * .5;
        foreach (var line in Lines)
        {
            for (var start = 0; start < line.Words.Length; start++)
            {
                var text = "";
                var first = line.Words[start];
                if (start > 0 && Token(line.Words[start - 1]) && Adjacent(line.Words[start - 1], first)) continue;
                for (var end = start; end < line.Words.Length; end++)
                {
                    var word = line.Words[end];
                    if (!Token(word)) break;
                    if (end > start)
                    {
                        var prior = line.Words[end - 1];
                        var gap = word.X - prior.X - prior.Width;
                        if (gap < 0 || gap > Math.Max(prior.Height, word.Height) * .6 || Math.Abs(word.Y - prior.Y) > Math.Max(prior.Height, word.Height) * .5) break;
                    }
                    text += word.Text;
                    if (text.Length > identity.Length) break;
                    if (!text.Equals(identity, StringComparison.OrdinalIgnoreCase)) continue;
                    if (end + 1 < line.Words.Length && Token(line.Words[end + 1]) && Adjacent(word, line.Words[end + 1])) break;
                    var candidate = new OcrWord(text, first.X, Math.Min(first.Y, word.Y), word.X + word.Width - first.X, Math.Max(first.Y + first.Height, word.Y + word.Height) - Math.Min(first.Y, word.Y));
                    if (candidate.X < 0 || candidate.Y < 0 || candidate.X + candidate.Width > Width || candidate.Y + candidate.Height > Height) break;
                    // Crop passes can read the same physical ID more than once.
                    if (!matches.Any(m => m.X < candidate.X + candidate.Width && candidate.X < m.X + m.Width && m.Y < candidate.Y + candidate.Height && candidate.Y < m.Y + m.Height)) matches.Add(candidate);
                    break;
                }
            }
        }
        return matches.ToArray();
    }
}
public enum LoginCheckpoint { Unknown, MemberList, MemberInformation, Connect, PasswordEditor, Home, Play, PlayConfirmation, Agreement, Title, Characters, CharacterConfirmation, Error }
public static class LoginCheckpoints
{
    public static OcrLine? PolMenuButton(LoginFrame frame, string label)
    {
        if (Recognize(frame) != LoginCheckpoint.MemberInformation || label is not ("Log In" or "Back")) return null;
        var candidates = frame.Lines.Where(l => LoginFrame.Normalize(l.Text) == LoginFrame.Normalize(label) && l.Words.Length > 0 &&
            l.Words.All(w => double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) &&
                w.Width > 0 && w.Height > 0 && w.X >= 0 && w.X + w.Width <= frame.Width * .3 &&
                w.Y >= frame.Height * .2 && w.Y + w.Height <= frame.Height * .75)).ToArray();
        if (candidates.Length == 0) return null;
        var boxes = candidates.Select(l => (Left: l.Words.Min(w => w.X), Right: l.Words.Max(w => w.X + w.Width),
            Top: l.Words.Min(w => w.Y), Bottom: l.Words.Max(w => w.Y + w.Height))).ToArray();
        if (boxes.Max(b => b.Left) >= boxes.Min(b => b.Right) || boxes.Max(b => b.Top) >= boxes.Min(b => b.Bottom)) return null;
        return candidates.OrderBy(l => l.Words.Max(w => w.X + w.Width) - l.Words.Min(w => w.X)).First();
    }
    public static OcrLine? PolDialogButton(LoginFrame frame, string label)
    {
        if (Recognize(frame) != LoginCheckpoint.Connect || label is not "Cancel" and not "Connect") return null;
        var matches = frame.Lines.SelectMany(l => l.Words).Where(w => LoginFrame.Normalize(w.Text) == LoginFrame.Normalize(label)).ToArray();
        if (matches.Length == 0 || matches.Any(w => !double.IsFinite(w.X) || !double.IsFinite(w.Y) ||
            !double.IsFinite(w.Width) || !double.IsFinite(w.Height) || w.Width <= 0 || w.Height <= 0 ||
            w.X < 0 || w.Y < 0 || w.X + w.Width > frame.Width || w.Y + w.Height > frame.Height)) return null;
        // "Connect" also occurs in the dialog title. Only captions in the
        // validated button row are actionable; titles cannot create ambiguity.
        matches = matches.Where(w => w.Y >= frame.Height * .5 && w.Y + w.Height <= frame.Height * .7).ToArray();
        if (matches.Length == 0) return null;
        var left = label == "Cancel" ? .6 : .16;
        var right = label == "Cancel" ? .86 : .39;
        if (matches.Any(w => w.X < frame.Width * left || w.X + w.Width > frame.Width * right ||
            w.Y < frame.Height * .5 || w.Y + w.Height > frame.Height * .7)) return null;
        // OCR can combine the three button captions into one line. Match the
        // actual word; duplicate crop readings must overlap one physical box.
        if (matches.Max(w => w.X) >= matches.Min(w => w.X + w.Width) ||
            matches.Max(w => w.Y) >= matches.Min(w => w.Y + w.Height)) return null;
        return new(label, [matches[0]]);
    }
    // The title menu accepts Enter as soon as Select Character appears; its
    // other labels and background are not required. This only opens the list.
    public static bool CanOpenCharacterList(LoginFrame frame)
    {
        if (Recognize(frame) is not LoginCheckpoint.Unknown and not LoginCheckpoint.Title ||
            frame.Has("Log in with") || frame.Has("Select a character to play")) return false;
        var control = frame.UniqueLine("Select Character");
        return control is { Words.Length: > 0 } && control.Words.All(w =>
            double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) &&
            w.Width > 0 && w.Height > 0 && w.X >= 0 && w.Y >= 0 &&
            w.X + w.Width <= frame.Width && w.Y + w.Height <= frame.Height);
    }
    // Opening the default character's dialog needs only the rendered footer,
    // not the counters or handwritten character details. This never accepts it.
    public static bool CanOpenDefaultCharacterConfirmation(LoginFrame frame)
    {
        if (Recognize(frame) is not LoginCheckpoint.Unknown and not LoginCheckpoint.Characters || frame.Has("Log in with")) return false;
        var footer = frame.UniqueLine("Select a character to play");
        return footer is { Words.Length: > 0 } && footer.Words.All(w =>
            double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) &&
            w.Width > 0 && w.Height > 0 && w.X >= 0 && w.X + w.Width <= frame.Width &&
            w.Y >= frame.Height * .85 && w.Y + w.Height <= frame.Height);
    }
    public static (double X, double Y)? ConfiguredCharacterRow(LoginFrame frame, string character)
    {
        if (string.IsNullOrWhiteSpace(character) || !CanOpenDefaultCharacterConfirmation(frame)) return null;
        var rows = frame.Lines.Where(l => LoginFrame.Normalize(l.Text) == LoginFrame.Normalize(character) && l.Words.Length > 0 &&
            l.Words.All(w => double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) &&
                w.Width > 0 && w.Height > 0 && w.X >= frame.Width * .65 && w.X + w.Width <= frame.Width &&
                w.Y >= frame.Height * .5 && w.Y + w.Height < frame.Height * .9))
            .Select(l => new { Left = l.Words.Min(w => w.X), Right = l.Words.Max(w => w.X + w.Width),
                Top = l.Words.Min(w => w.Y), Bottom = l.Words.Max(w => w.Y + w.Height) }).ToArray();
        if (rows.Length == 0 || rows.Max(r => r.Left) >= rows.Min(r => r.Right) || rows.Max(r => r.Top) >= rows.Min(r => r.Bottom)) return null;
        return ((rows[0].Left + rows[0].Right) / 2, (rows[0].Top + rows[0].Bottom) / 2);
    }
    public static string? MemberCardLabel(LoginFrame frame, string identity) => MemberCardLabels(frame, identity).FirstOrDefault();
    public static string[] MemberCardLabels(LoginFrame frame, string identity)
    {
        if (Recognize(frame) != LoginCheckpoint.MemberList || frame.IdentityCandidates(identity) is not [var id]) return [];
        var labels = frame.Lines.Where(l => Regex.IsMatch(l.Text.Trim(), "^[A-Za-z0-9 _.-]+$") && l.Words.Length > 0 && l.Words.All(w =>
            w.X >= frame.Width * .4 && w.X + w.Width <= frame.Width * .95 &&
            w.Y > id.Y + id.Height && w.Y < id.Y + id.Height * 3.5)).ToArray();
        if (labels.Length == 0) return [];
        // Different crop readings may disagree on a glyph in the same local
        // label. They still describe one physical caption, selected by exact ID.
        // Preserve distinct exact readings of this one physical caption for
        // corroboration on the next screen; never compare IDs fuzzily.
        var boxes = labels.Select(l => (Left: l.Words.Min(w => w.X), Right: l.Words.Max(w => w.X + w.Width),
            Top: l.Words.Min(w => w.Y), Bottom: l.Words.Max(w => w.Y + w.Height))).ToArray();
        if (boxes.Max(b => b.Left) >= boxes.Min(b => b.Right) || boxes.Max(b => b.Top) >= boxes.Min(b => b.Bottom)) return [];
        return labels.Where(l => frame.UniqueLine(l.Text) != null).Select(l => l.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // This corroborates a card selected by its exact SE ID in the same run.
    // A member label by itself is never account identity evidence.
    public static bool SelectedMemberLabelMatches(LoginFrame frame, string label)
    {
        return Recognize(frame) == LoginCheckpoint.MemberInformation && MemberLabelGeometryMatches(frame, label);
    }
    public static bool SelectedMemberLabelBehindConnectMatches(LoginFrame frame, string label) =>
        Recognize(frame) == LoginCheckpoint.Connect && MemberLabelGeometryMatches(frame, label);
    private static bool MemberLabelGeometryMatches(LoginFrame frame, string label)
    {
        if (frame.UniqueLine(label) is not { Words.Length: > 0 } member ||
            frame.UniqueLine("Square Enix ID") is not { Words.Length: > 0 } field) return false;
        var bottom = field.Words.Max(w => w.Y + w.Height);
        var height = field.Words.Max(w => w.Height);
        // UniqueLine already requires the exact normalized caption; OCR word
        // spacing may differ between the card and details without changing it.
        return member.Words.All(w =>
            w.X >= frame.Width * .35 && w.X + w.Width <= frame.Width * .95 && w.Y > bottom && w.Y < bottom + height * 2.5);
    }
    public static OcrLine? ConfirmationButton(LoginFrame frame, string label)
    {
        if (label is not "Yes" and not "No" ||
            Recognize(frame) != LoginCheckpoint.CharacterConfirmation &&
            !(label == "No" && Recognize(frame) == LoginCheckpoint.Unknown && frame.Has("Log in with"))) return null;
        var normalized = LoginFrame.Normalize(label);
        var words = frame.Lines.Where(l => LoginFrame.Normalize(l.Text) == normalized)
            .SelectMany(l => l.Words).Where(w => LoginFrame.Normalize(w.Text) == normalized).ToArray();
        if (words.Length == 0 || words.Any(w => !double.IsFinite(w.X) || !double.IsFinite(w.Y) || !double.IsFinite(w.Width) || !double.IsFinite(w.Height) || w.Width <= 0 || w.Height <= 0 || w.X < 0 || w.Y < 0 || w.X + w.Width > frame.Width || w.Y + w.Height > frame.Height)) return null;
        // Different OCR crops can include decorative strokes beside one caption.
        // Merge only readings whose caption boxes all physically overlap. Two
        // distinct controls remain ambiguous, even when their text is identical.
        if (words.Max(w => w.X) >= words.Min(w => w.X + w.Width) || words.Max(w => w.Y) >= words.Min(w => w.Y + w.Height)) return null;
        var caption = words.OrderBy(w => w.Text.Equals(label, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(w => w.Width).First();
        return new(label, [caption]);
    }
    public static (double X, double Y)? ConfirmationCancelPoint(LoginFrame frame, string character)
    {
        if (!ConfirmsCharacter(frame, character) || Recognize(frame) is not (LoginCheckpoint.CharacterConfirmation or LoginCheckpoint.Unknown)) return null;
        if (ConfirmationButton(frame, "No") is { Words: [var no] })
        {
            var nx = no.X + no.Width / 2;
            var ny = no.Y + no.Height / 2;
            return nx > frame.Width * .55 && nx < frame.Width * .8 && ny >= frame.Height * .5 && ny <= frame.Height * .65 ? (nx, ny) : null;
        }
        if (Recognize(frame) != LoginCheckpoint.CharacterConfirmation) return null;
        if (frame.Lines.Any(l => LoginFrame.Normalize(l.Text) == "no")) return null; // Ambiguous/invalid evidence is not a missing caption.
        // The game confirmation has a fixed pair of buttons. Anchor the
        // missing No caption to the detected Yes glyph size, not desktop pixels.
        if (ConfirmationButton(frame, "Yes") is not { Words: [var yes] }) return null;
        var scale = Math.Round(yes.Height / 14 * 4) / 4;
        var x = yes.X + yes.Width / 2;
        var y = yes.Y + yes.Height / 2;
        if (!double.IsFinite(scale) || scale <= 0 || x < frame.Width * .4 || x > frame.Width * .65 || y < frame.Height * .5 || y > frame.Height * .65) return null;
        x += 128 * scale;
        return x > frame.Width * .55 && x < frame.Width * .8 ? (x, y) : null;
    }
    public static (double X, double Y)? InitialCharacterCandidate(LoginFrame frame)
    {
        if (Recognize(frame) != LoginCheckpoint.Characters) return null;
        var footer = frame.UniqueLine("Select a character to play");
        var anchor = footer?.Words.Where(w => w.Text.Equals("Select", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (anchor is not [var word] || word.Height <= 0) return null;
        // The character grid is anchored to the bottom-right of the FFXI UI.
        // Its size follows the footer font, independently of the desktop DPI.
        // OCR glyph boxes vary slightly between frames; stabilize the UI scale.
        var scale = Math.Round(word.Height / 14 * 4) / 4;
        var x = frame.Width - 290 * scale;
        var y = word.Y + word.Height / 2 - 192 * scale;
        if (!double.IsFinite(scale) || scale <= 0 || x < frame.Width * .65 || x > frame.Width * .95 || y < frame.Height * .5 || y > frame.Height * .85) return null;
        return (x, y);
    }
    public static (double X, double Y)? PasswordField(LoginFrame frame)
    {
        if (Recognize(frame) != LoginCheckpoint.Connect) return null;
        var label = frame.UniqueLine("Square Enix Password");
        if (label == null || label.Words.Length == 0) return null;
        var right = label.Words.Max(w => w.X + w.Width);
        var top = label.Words.Min(w => w.Y);
        var bottom = label.Words.Max(w => w.Y + w.Height);
        // POL's fixed dialog places this field to the right of the detected label.
        // Use client dimensions and the label's actual row, never desktop coordinates.
        var x = frame.Width * 0.705;
        var y = (top + bottom) / 2;
        if (right >= frame.Width * 0.6 || y < frame.Height * 0.3 || y > frame.Height * 0.55 ||
            label.Words.Any(w => w.X < frame.Width * 0.1)) return null;
        return (x, y);
    }
    public static bool IsCredentialRejection(LoginFrame frame) =>
        frame.Has("POL-5311") || frame.Has("password entered is incorrect") || frame.Has("Incorrect password") || frame.Has("Invalid password") || frame.Has("Wrong password") ||
        frame.Has("password is incorrect") || frame.Has("password is invalid") || frame.Has("ID or password are incorrect");
    public static LoginCheckpoint Recognize(LoginFrame frame)
    {
        // Error checks precede actionable screens. Maintenance news on the home page is not an error.
        if (frame.Has("Error code") || IsCredentialRejection(frame) || frame.Has("Connection failed")) return LoginCheckpoint.Error;
        if (frame.Has("Log in with") && frame.Has("Yes") && frame.Has("No")) return LoginCheckpoint.CharacterConfirmation;
        if (frame.Has("Please enter your password") && frame.Has("software keyboard")) return LoginCheckpoint.PasswordEditor;
        if (frame.Has("Connect to PlayOnline") && frame.Has("Square Enix Password")) return LoginCheckpoint.Connect;
        if (frame.Has("Member List") && frame.Has("Add Member")) return LoginCheckpoint.MemberList;
        if (frame.Has("Member Information") && frame.Has("Login Information") && frame.Has("Log In")) return LoginCheckpoint.MemberInformation;
        if (frame.Has("A Word to Our Players") && frame.Has("Play") && frame.Has("Back")) return LoginCheckpoint.PlayConfirmation;
        if (frame.Has("Accept") && (frame.Has("Decline") || frame.Has("User Agreement")) && (frame.Has("FINAL FANTASY XI") || frame.Has("Rules of Conduct"))) return LoginCheckpoint.Agreement;
        if (frame.Has("Select Character") && frame.Has("Create Character") && frame.Has("Delete Character")) return LoginCheckpoint.Title;
        if (frame.Has("Navigator") && frame.Has("Log Out") && frame.Has("FINAL FANTASY")) return LoginCheckpoint.Home;
        if (frame.Has("Play") && frame.Has("Game Manual") && frame.Has("Support Center")) return LoginCheckpoint.Play;
        if (frame.Has("Select a character to play") && (frame.Has("characters registered") || frame.Has("characters can be made"))) return LoginCheckpoint.Characters;
        return LoginCheckpoint.Unknown;
    }
    public static bool SelectedIdentity(LoginFrame frame, string character, string world)
    {
        bool Exact(string a, string b) => a.Trim('"', '\'', '?', '.', ',', ':', '“', '”').Equals(b, StringComparison.OrdinalIgnoreCase);
        var lines = frame.Lines.Where(l => l.Words.Length > 0 && l.Words.All(w => w.X < frame.Width * 0.25 && w.Y > frame.Height * 0.5 && w.Y < frame.Height * 0.9)).ToArray();
        var name = lines.Count(l => LoginFrame.Normalize(l.Text) == LoginFrame.Normalize(character));
        var worldField = lines.Count(l => l.Words.Length == 2 && Exact(l.Words[0].Text, "World") && Exact(l.Words[1].Text, world));
        return name == 1 && worldField == 1;
    }
    public static bool ConfirmsCharacter(LoginFrame frame, string character)
    {
        // A crop can read only the instruction and omit the entire name. It
        // carries no identity evidence; partially read names still conflict.
        var prompts = frame.Lines.Select(l => LoginFrame.Normalize(l.Text)).Where(t => t.StartsWith("loginwith", StringComparison.Ordinal) && t != "loginwith").Distinct().ToArray();
        return prompts is [var prompt] && prompt == "loginwith" + LoginFrame.Normalize(character);
    }
    public static bool HasConflictingConfirmation(LoginFrame frame, string character) => frame.Lines
        .Select(l => LoginFrame.Normalize(l.Text))
        .Any(t => t.StartsWith("loginwith", StringComparison.Ordinal) && t != "loginwith" && t != "loginwith" + LoginFrame.Normalize(character));
    public static string? Requirements(AccountConfiguration account)
    {
        if (string.IsNullOrWhiteSpace(account.SquareEnixId)) return "Configure the Square Enix ID.";
        if (!Regex.IsMatch(account.Character, "^[A-Za-z]+$") || !WorldCatalog.Names.Contains(account.World, StringComparer.OrdinalIgnoreCase)) return "Configure the target character and world.";
        if (account.UsesOneTimePassword) return "One-time password accounts need interactive login.";
        if (!account.PolPasswordStoredInPol || account.MemberPasswordRequired) return "This login flow requires POL to store its password and no member password.";
        return null;
    }
}
