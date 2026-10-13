// The browser is the device; Web Locks separately ensure only one tab can use its lease.
export function browserLeaseId(storage, session, uuid) {
    const key='wcg.cloud.device';
    const valid=value=>typeof value==='string'&&/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(value);
    const saved=storage.getItem(key), previous=session.getItem(key);
    const id=valid(saved)?saved:valid(previous)?previous:uuid();
    storage.setItem(key,id);
    session.setItem(key,id);
    return id;
}
