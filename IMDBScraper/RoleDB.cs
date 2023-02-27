using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IMDBScraper
{
    public class RoleDB
    {
        HashSet<RoleKey> _roles = new HashSet<RoleKey>();
        Dictionary<long, Role> _rolesById = new Dictionary<long, Role>();
        Random _rand = new Random();

        public bool Has(long id) => _rolesById.ContainsKey(id);

        public Role this[long id]
        {
            get
            {
                lock (_roles)
                {
                    return _rolesById[id];
                }
            }
        }

        public List<Role> GetFullManifest()
        {
            lock(_roles)
            {
                return _roles.Cast<Role>().ToList();
            }
        }

        private long UnusedId()
        {
            lock (_roles)
            {
                long id;
                for (id = _rand.NextInt64(); _rolesById.ContainsKey(id); id++) ;
                return id;
            }
        }

        private RoleKey KeyOf(Role role)
        {
            return new RoleKey(role.type, role.show, role.talent);
        }

        public Role GetOrCreate(RoleType type, long showId, long personId)
        {
            var key = new RoleKey(type, showId, personId);

            lock (_roles)
            {
                if (_roles.TryGetValue(key, out var existing))
                    return (Role)existing;

                var newRole = new Role
                {
                    type = type,
                    show = showId,
                    talent = personId,
                    id = UnusedId()
                };

                _roles.Add(newRole);
                _rolesById[newRole.id] = newRole;
                return newRole;
            }
        }

        public void Remove(Role role)
        {
            lock (_roles)
            {
                _roles.Remove(KeyOf(role));
                _rolesById.Remove(role.id);
            }
        }

        public async Task Save(string path)
        {
            List<Role> toSave;
            lock (_roles)
            {
                toSave = _roles.Cast<Role>().ToList();
            }

            using (var stream = await IMDBReader.PatientOpenWrite(path))
            {
                Json.Serialize(stream, toSave);
            }
        }

        public void Load(string path)
        {
            lock (_roles)
            {
                _roles.Clear();
                _rolesById.Clear();

                if (File.Exists(path))
                {
                    using (var file = File.OpenRead(path))
                    {
                        var loadedRoles = Json.Deserialize<List<Role>>(file) ?? new List<Role>();

                        if (loadedRoles == null) return;

                        foreach (var role in loadedRoles)
                        {
                            _rolesById[role.id] = role;
                            _roles.Add(role);
                        }
                    }
                }
            }
        }
    }
}
