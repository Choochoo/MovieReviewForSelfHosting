using System.Text.RegularExpressions;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Shared service for analyzing words in transcripts using comprehensive pattern matching.
/// Provides consistent word detection across all services.
/// </summary>
public class WordAnalysisService
{
    // Laughter and positive expressions (non-offensive)
    private static readonly string[] LaughterPatterns = new[]
    {
        "haha", "hehe", "hihi", "hoho", "hahaha", "ahahaha",
        "lol", "lmao", "lmfao", "rofl", "roflmao",
        "laughing", "chuckle", "chuckling", "giggle", "giggling",
        "snicker", "chortle", "guffaw", "cackle", "titter",
        "😂", "🤣", "😆", "😄", "😃", "😁",
        "kek", "jaja", "xD", "XD", ":D", "=D",
        "teehee", "har har", "funny", "hilarious", "amusing",
        "omg", "lmfaooo", "dead", "dying", "rolling"
    };

    // Mild profanity (generally considered less offensive)
    private static readonly string[] MildProfanityPatterns = new[]
    {
        "damn", "dammit", "damnit", "hell", "heck", "crap", "piss", "pissed", "bloody", "bugger",
        "arse", "ass", "bastard", "bitch", "bollocks", "pissing", "pisses", "crappy", "crappier", "crappiest",
        "freaking", "freakin", "frickin", "fricking", "jerk", "jerks", "jerkoff", "boob", "boobs", "tit",
        "tits", "titty", "titties", "prick", "pricks", "suck", "sucks", "sucked", "sucking", "sucky",
        "blows", "blow", "blowing", "screwed", "screw", "screwing", "screws", "gosh", "darn", "darned",
        "dang", "dangit", "shoot", "shucks", "snap", "fudge", "crud", "cripes", "crikey", "geez",
        "jeez", "cheese", "rats", "nuts", "blast", "drat", "phooey", "poppycock", "baloney", "hogwash",
        "malarkey", "rubbish", "codswallop", "hooey", "bull", "bulls", "bullcrap", "horsecrap", "horseshit",
        "chickenshit", "dogshit", "cowshit", "batshit", "apeshit", "dipshit", "horsepucky", "balderdash",
        "gobbledygook", "fiddlesticks", "piffle", "twaddle", "nincompoop", "dimwit", "nitwit", "halfwit",
        "blockhead", "bonehead", "numbskull", "knucklehead", "meathead", "airhead", "birdbrain", "peabrain",
        "pinhead", "lamebrain", "scatterbrain", "butthead", "dickhead", "shithead", "crackhead", "pothead",
        "deadhead", "egghead", "fathead", "hothead", "redhead", "bighead", "hardhead", "softhead",
        "thickhead", "metalhead", "cheesehead", "sleepyhead", "loggerhead", "bullethead", "hammerhead", "dickwad",
        "dickweed", "asshat", "asswipe", "buttface", "butthole", "buttwipe", "dipstick", "dingleberry", "doofus",
        "dumbo", "dummy", "dunce", "goofball", "knobhead", "lunkhead", "moronhead", "nutcase", "nutjob",
        "screwball", "wacko", "whackjob", "bonkers", "crackers", "loony", "loopy", "nutty", "screwy",
        "wacky", "bananas", "batty", "daffy", "dotty", "fruity", "goofy", "kooky", "silly", "zany",
        "absurd", "ridiculous", "preposterous", "outrageous", "ludicrous", "nonsensical", "foolish", "idiotic", "moronic", "stupid",
        "dumb", "dense", "thick", "slow", "simple", "dopey", "ditzy", "ditsy", "scatterbrained", "featherbrained",
        "lightheaded", "empty-headed", "weak-minded", "feeble-minded", "simple-minded", "narrow-minded", "small-minded", "closed-minded", "pig-headed", "mule-headed"
    };

    // Strong profanity (more offensive)
    private static readonly string[] StrongProfanityPatterns = new[]
    {
        "fuck", "fucking", "fucker", "fucked", "fck", "fuk", "fuckin", "fucken", "fuckn", "fuckng",
        "fucks", "fucka", "fuckah", "fuckboy", "fuckface", "fuckhead", "fuckhole", "fucktard", "fucktoy", "fuckup",
        "fuckwad", "fuckwit", "motherfuck", "motherfucker", "motherfuckin", "motherfucking", "clusterfuck", "buttfuck", "mindfuck", "brainfuck",
        "shit", "shitty", "bullshit", "shite", "sht", "shits", "shitted", "shitting", "shithead", "shitface",
        "shithole", "shitstorm", "shitshow", "shitload", "shitbag", "shithouse", "shitless", "horseshit", "ratshit", "batshit",
        "apeshit", "dogshit", "chickenshit", "cowshit", "catshit", "pigshit", "birdshit", "fishsticks", "fishstick", "bullcrap",
        "cock", "cocks", "cockhead", "cockface", "cocksucker", "cocksucking", "cockblock", "cockblocker", "rooster", "cocky",
        "dick", "dicks", "dickin", "dicking", "dickish", "dickwad", "dickweed", "dickhead", "dickface", "dickhole",
        "dickless", "dickbag", "dickbreath", "dicksmack", "dickslap", "bigdick", "smalldick", "tinydick", "limp-dick", "flaccid",
        "prick", "pricks", "pricking", "pricked", "prickish", "prickhead", "prickface", "prickhole", "prickly", "smartprick",
        "twat", "twats", "twatty", "twatface", "twathead", "twathole", "twatbag", "twatwaffle", "fudgepacker", "rugmuncher",
        "cunt", "cunts", "cunty", "cuntface", "cunthead", "cunthole", "cuntbag", "cuntlicker", "scumbag", "douchebag",
        "asshole", "assholes", "asshat", "asswipe", "assface", "asshead", "asslicker", "assmunch", "assmonkey", "jackass",
        "smartass", "dumbass", "fatass", "tightass", "hardass", "badass", "wiseass", "ass-kisser", "kiss-ass", "half-ass",
        "bitch", "bitches", "bitchy", "bitchin", "bitching", "bitchface", "bitchass", "bitchboy", "sonofabitch", "son-of-a-bitch",
        "whore", "whores", "whorish", "whoring", "slut", "sluts", "slutty", "slutface", "slutbag", "cumslut",
        "bastard", "bastards", "dirtbag", "scumbag", "lowlife", "piece-of-shit", "pos", "goddamn", "goddamnit", "goddam",
        "jesus-christ", "christ-almighty", "holy-shit", "holy-fuck", "what-the-fuck", "wtf", "what-the-hell", "wth", "son-of-a-gun", "piece-of-crap"
    };

    // Pejorative and derogatory terms (insulting/demeaning language)
    private static readonly string[] PejorativePatterns = new[]
    {
        // Intellectual/mental capacity insults
        "stupid", "idiot", "moron", "dumb", "dumbass", "dimwit", "airhead", "brainless",
        "retard", "retarded", "lame", "pathetic", "clueless", "dense", "thick", "slow",
        
        // Character/worth insults
        "loser", "worthless", "useless", "garbage", "trash", "scum", "waste", "failure",
        "reject", "lowlife", "deadbeat", "bum", "slob", "sleaze",
        
        // Appearance/physical insults
        "ugly", "hideous", "fat", "fatty", "skinny", "gross", "disgusting", "repulsive",
        "revolting", "nasty", "filthy", "dirty", "smelly", "stinky", "fugly", "butt-ugly",
        
        // Behavioral/personality insults
        "freak", "weirdo", "creep", "creepy", "sick", "twisted", "psycho", "crazy",
        "insane", "mental", "nuts", "nutcase", "lunatic", "maniac", "basket case",
        
        // Size/stature insults
        "shorty", "midget", "giant", "beast", "pig", "cow", "whale", "twig",
        
        // Sexual/gender-based insults (general inappropriate terms)
        "slut", "whore", "skank", "hoe", "thot", "tramp", "floozy", "tart",
        
        // Social/personality insults
        "nerd", "geek", "dork", "wimp", "weakling", "coward", "chicken", "sissy", "baby",
        "brat", "spoiled", "entitled", "selfish", "greedy", "cheap", "stingy",
        
        // Modern internet/social media insults
        "annoying", "irritating", "obnoxious", "toxic", "cringe", "cringey", "basic",
        "try-hard", "poser", "fake", "phony", "wannabe", "normie", "edgelord",
        
        // Additional general pejoratives
        "awful", "terrible", "horrible", "dreadful", "atrocious", "abysmal", "pitiful",
        "miserable", "sad", "pathetic", "lame", "boring", "tedious", "bland", "vanilla",

        "gay", "fag", "faggot", "tranny", "lady boy", "ladyboy"
    };

    // Combined regex patterns for performance
    private static readonly Regex LaughterRegex = new(
        @"\b(" + string.Join("|", LaughterPatterns.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MildProfanityRegex = new(
        @"\b(" + string.Join("|", MildProfanityPatterns.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StrongProfanityRegex = new(
        @"\b(" + string.Join("|", StrongProfanityPatterns.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PejorativeRegex = new(
        @"\b(" + string.Join("|", PejorativePatterns.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Analyzes text for laughter indicators.
    /// </summary>
    public MatchCollection FindLaughter(string text)
    {
        return LaughterRegex.Matches(text);
    }

    /// <summary>
    /// Analyzes text for mild profanity.
    /// </summary>
    public MatchCollection FindMildProfanity(string text)
    {
        return MildProfanityRegex.Matches(text);
    }

    /// <summary>
    /// Analyzes text for strong profanity.
    /// </summary>
    public MatchCollection FindStrongProfanity(string text)
    {
        return StrongProfanityRegex.Matches(text);
    }

    /// <summary>
    /// Analyzes text for pejorative terms.
    /// </summary>
    public MatchCollection FindPejoratives(string text)
    {
        return PejorativeRegex.Matches(text);
    }

    /// <summary>
    /// Analyzes text for all curse words (mild + strong profanity).
    /// </summary>
    public (MatchCollection mild, MatchCollection strong) FindAllProfanity(string text)
    {
        return (FindMildProfanity(text), FindStrongProfanity(text));
    }
}