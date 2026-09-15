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
  },
  startBrowserDownload: function (url) {
    const iframe = document.createElement("iframe");
    iframe.style.display = "none";
    iframe.src = url;
    document.body.appendChild(iframe);
    setTimeout(() => iframe.remove(), 60000);
  },
  downloadRemoteFile: async function (url, fileName) {
    const response = await fetch(url, { credentials: "omit" });
    if (!response.ok) {
      throw new Error("DOWNLOAD_FAILED");
    }

    const blob = await response.blob();
    const link = document.createElement("a");
    link.download = fileName;
    link.href = URL.createObjectURL(blob);
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(link.href);
  }
};
