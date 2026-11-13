// Offline unit test of libkan/sha256.hpp against the published FIPS 180-4
// vectors (known input -> expected output, repo rule). The digest guards
// the model-download pin (transcribe.hpp, audit C4), so a wrong
// implementation must fail here first. No network, no model download.
#include "libkan/sha256.hpp"

#include <cstdio>
#include <string>

namespace {

bool pass = true;

void expect(const std::string& got, const std::string& want, const char* what) {
    const bool ok = got == want;
    printf("%s: %s\n", ok ? "ok  " : "FAIL", what);
    if (!ok) printf("     got:  %s\n     want: %s\n", got.c_str(), want.c_str());
    pass &= ok;
}

} // namespace

int main() {
    // FIPS 180-4 / NIST example vectors.
    expect(kan::sha256::hex_of("", 0),
           "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
           "empty input");
    expect(kan::sha256::hex_of("abc", 3),
           "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
           "abc");
    expect(kan::sha256::hex_of("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", 56),
           "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
           "448-bit two-block message");
    expect(kan::sha256::hex_of(
               "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmno"
               "ijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu", 112),
           "cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1",
           "896-bit two-block message");

    // One million 'a' exercises the multi-block path at scale.
    {
        kan::sha256::Ctx c;
        kan::sha256::init(c);
        const std::string chunk(1000, 'a');
        for (int i = 0; i < 1000; ++i) kan::sha256::update(c, chunk.data(), chunk.size());
        expect(kan::sha256::final_hex(c),
               "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0",
               "one million 'a'");
    }

    // Chunking must not matter (the model file is hashed in 64 KiB reads).
    {
        const std::string msg = "the kan! separation model pin; 0123456789; "
                                "the kan! separation model pin; 0123456789";
        kan::sha256::Ctx c;
        kan::sha256::init(c);
        for (size_t i = 0; i < msg.size(); i += 7) {
            const size_t n = (msg.size() - i < 7) ? msg.size() - i : 7;
            kan::sha256::update(c, msg.data() + i, n);
        }
        expect(kan::sha256::final_hex(c), kan::sha256::hex_of(msg.data(), msg.size()),
               "chunked update equals one-shot");
    }

    // File hashing matches in-memory hashing (and a missing file answers "").
    {
        const std::string path = "kan_sha256_test_input.bin";
        const std::string body = "model bytes go here\n";
        std::FILE* f = std::fopen(path.c_str(), "wb");
        pass &= (f != nullptr);
        if (f != nullptr) {
            std::fwrite(body.data(), 1, body.size(), f);
            std::fclose(f);
            expect(kan::sha256::file_hex(path), kan::sha256::hex_of(body.data(), body.size()),
                   "file_hex equals hex_of");
            std::remove(path.c_str());
        }
        expect(kan::sha256::file_hex("kan_sha256_no_such_file.bin"), "",
               "missing file answers empty");
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
