// Runs in Oriel's isolated world before the page: the page gets one string channel to the host and nothing else.
const { ipcRenderer, contextBridge } = oriel;

contextBridge.exposeInMainWorld('khostScreen', {
    send: (json) => ipcRenderer.send('screen', json),
    receive: (callback) => ipcRenderer.on('screen', (_event, json) => callback(json)),
});
