# Markdown Support for Movie Reasoning

The Movie Review App now supports Markdown formatting in the movie reasoning field. This allows users to create rich, formatted text when explaining their movie choices.

## Supported Markdown Features

### Text Formatting
- **Bold text**: Use `**text**` or `__text__`
- *Italic text*: Use `*text*` or `_text_`
- ***Bold and italic***: Use `***text***`
- ~~Strikethrough~~: Use `~~text~~`

### Links
- [Link text](url): Use `[text](url)`
- Automatic link detection for URLs

### Lists
**Unordered lists:**
```markdown
- Item 1
- Item 2
  - Nested item
```

**Ordered lists:**
```markdown
1. First item
2. Second item
   1. Nested item
```

### Quotes
> Block quotes: Use `> text`

### Code
- Inline code: Use \`code\`
- Code blocks: Use triple backticks
```
```code
Multi-line code block
```
```

### Other Features
- Headings: Use `#`, `##`, `###`, etc.
- Horizontal rules: Use `---` or `***`
- Line breaks: End a line with two spaces or use `<br>`

## Usage

### When Editing
1. Enter your reasoning text using Markdown syntax
2. Click the "Preview" tab to see how it will look
3. Switch back to "Edit" to make changes
4. Save when satisfied

### Display Locations
Markdown-formatted reasoning is displayed in:
- The main phase view on the home page
- The history page (both list and theater views)
- The movie details modal in theater view

## Examples

**Movie reasoning with multiple points:**
```markdown
I chose this movie because:
- It's a **classic** that everyone should see
- The cinematography is *breathtaking*
- It features [amazing performances](https://www.imdb.com/title/tt0111161/)

> "Get busy living, or get busy dying." - One of my favorite quotes
```

**Technical movie analysis:**
```markdown
This film uses several interesting techniques:

1. **Long takes** - Some shots last over 5 minutes
2. **Natural lighting** - No artificial lights were used
3. **Improvised dialogue** - Actors created their own lines

The director said in an interview:
> The goal was to create something that felt real and immediate

`Fun fact:` The entire movie was shot in chronological order.
```