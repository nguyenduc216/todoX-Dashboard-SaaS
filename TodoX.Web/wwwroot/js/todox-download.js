window.todoxDownload = {
  saveBase64File: function (fileName, contentType, base64) {
    const link = document.createElement("a");
    link.download = fileName;
    link.href = `data:${contentType};base64,${base64}`;
    document.body.appendChild(link);
    link.click();
    link.remove();
  },
  saveTextFile: function (fileName, text) {
    const blob = new Blob([text || ""], { type: "text/plain;charset=utf-8" });
    const link = document.createElement("a");
    link.download = fileName;
    link.href = URL.createObjectURL(blob);
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(link.href);
  }
};
