window.todoxImageEditor = {
    exportEditedImage: async function (imageId, rotation, cropSquare, mimeType) {
        const img = document.getElementById(imageId);
        if (!img) {
            throw new Error("Image element not found.");
        }

        if (!img.complete) {
            await new Promise((resolve, reject) => {
                img.onload = resolve;
                img.onerror = () => reject(new Error("Image load failed."));
            });
        }

        const source = await createImageBitmap(img);
        let sx = 0;
        let sy = 0;
        let sw = source.width;
        let sh = source.height;

        if (cropSquare) {
            const side = Math.min(source.width, source.height);
            sx = Math.floor((source.width - side) / 2);
            sy = Math.floor((source.height - side) / 2);
            sw = side;
            sh = side;
        }

        const normalizedRotation = ((rotation % 360) + 360) % 360;
        const swap = normalizedRotation === 90 || normalizedRotation === 270;
        const canvas = document.createElement("canvas");
        canvas.width = swap ? sh : sw;
        canvas.height = swap ? sw : sh;

        const ctx = canvas.getContext("2d");
        ctx.save();
        ctx.translate(canvas.width / 2, canvas.height / 2);
        ctx.rotate(normalizedRotation * Math.PI / 180);
        ctx.drawImage(source, sx, sy, sw, sh, -sw / 2, -sh / 2, sw, sh);
        ctx.restore();
        source.close?.();

        return canvas.toDataURL(mimeType || "image/png", 0.95);
    }
};
