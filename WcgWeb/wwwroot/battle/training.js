const key = 'wcg.trainingScene.v1';
export function readScene() { return localStorage.getItem(key); }
export function saveScene(scene, expected) {
    if (localStorage.getItem(key) !== expected) throw new Error('測試場面已由其他頁面更新，請重新載入後再保存。');
    localStorage.setItem(key, scene);
}
