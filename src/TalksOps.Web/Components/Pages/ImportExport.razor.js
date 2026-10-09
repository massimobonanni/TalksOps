export async function downloadFile(fileName, contentStreamReference) {
    const buffer = await contentStreamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: "application/json" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}
