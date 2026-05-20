let textareaRef = null;
let gutterRef = null;

export function init(textarea, gutter) {
    textareaRef = textarea;
    gutterRef = gutter;

    textarea.addEventListener("keydown", handleKeyDown);
    textarea.addEventListener("scroll", syncScroll);
}

export function getActiveLine(textarea) {
    const value = textarea.value;
    const pos = textarea.selectionStart;
    const lines = value.substring(0, pos).split("\n");
    return lines.length;
}

function syncScroll() {
    if (gutterRef && textareaRef) {
        gutterRef.scrollTop = textareaRef.scrollTop;
    }
}

function handleKeyDown(e) {
    const textarea = e.target;

    if (e.key === "Tab") {
        e.preventDefault();
        const start = textarea.selectionStart;
        const end = textarea.selectionEnd;
        const value = textarea.value;

        textarea.value = value.substring(0, start) + "  " + value.substring(end);
        textarea.selectionStart = textarea.selectionEnd = start + 2;
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
    }

    if (e.key === "Enter") {
        e.preventDefault();
        const start = textarea.selectionStart;
        const value = textarea.value;

        // Get current line's indentation
        const lineStart = value.lastIndexOf("\n", start - 1) + 1;
        const line = value.substring(lineStart, start);
        const indent = line.match(/^(\s*)/)[1];

        // Check if we're after an opening bracket
        const charBefore = value[start - 1];
        const charAfter = value[start];
        let insert = "\n" + indent;

        if (charBefore === "{" || charBefore === "[") {
            insert = "\n" + indent + "  ";
            if (charAfter === "}" || charAfter === "]") {
                insert += "\n" + indent;
                textarea.value = value.substring(0, start) + insert + value.substring(start);
                textarea.selectionStart = textarea.selectionEnd = start + indent.length + 3;
            } else {
                textarea.value = value.substring(0, start) + insert + value.substring(start);
                textarea.selectionStart = textarea.selectionEnd = start + insert.length;
            }
        } else {
            textarea.value = value.substring(0, start) + insert + value.substring(start);
            textarea.selectionStart = textarea.selectionEnd = start + insert.length;
        }

        textarea.dispatchEvent(new Event("input", { bubbles: true }));
    }

    // Auto-close brackets
    if (e.key === "{" || e.key === "[" || e.key === '"') {
        const start = textarea.selectionStart;
        const end = textarea.selectionEnd;
        const value = textarea.value;
        const closers = { "{": "}", "[": "]", '"': '"' };
        const closer = closers[e.key];

        if (start === end) {
            e.preventDefault();
            textarea.value = value.substring(0, start) + e.key + closer + value.substring(end);
            textarea.selectionStart = textarea.selectionEnd = start + 1;
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
        }
    }
}
