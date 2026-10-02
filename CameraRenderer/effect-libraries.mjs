export function buildAvatarEffectLibraries(effects) {
    const libraries = new Map();
    for (const effect of effects) {
        if (!effect || effect.id == null || typeof effect.lib !== 'string' || !effect.lib.length) continue;
        const id = String(effect.id);
        const mapped = libraries.get(id) ?? [];
        if (!mapped.includes(effect.lib)) mapped.push(effect.lib);
        libraries.set(id, mapped);
    }
    return libraries;
}

export function avatarEffectsReady(users, libraries, isLoaded) {
    let ready = true;
    for (const user of users) {
        if (!user.effect) continue;
        const mapped = libraries.get(String(user.effect));
        if (!mapped?.length) throw new Error(`Missing avatar effect mapping ${user.effect}`);
        for (const library of mapped) if (!isLoaded(library)) ready = false;
    }
    return ready;
}
