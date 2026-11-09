package gameplay

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"unicode/utf16"
	"unicode/utf8"
)

func DecodeJSON(data []byte) (any, error) {
	if len(data) > 1048576 || !utf8.Valid(data) {
		return nil, errors.New("invalid JSON size or encoding")
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	value, err := readJSON(decoder, 0)
	if err != nil {
		return nil, err
	}
	if _, err := decoder.Token(); err != io.EOF {
		return nil, errors.New("trailing JSON content")
	}
	return value, nil
}

func readJSON(decoder *json.Decoder, depth int) (any, error) {
	if depth > 32 {
		return nil, errors.New("JSON nesting limit")
	}
	token, err := decoder.Token()
	if err != nil {
		return nil, err
	}
	if delimiter, ok := token.(json.Delim); ok {
		switch delimiter {
		case '{':
			object := map[string]any{}
			for decoder.More() {
				keyToken, err := decoder.Token()
				if err != nil {
					return nil, err
				}
				key, ok := keyToken.(string)
				if !ok {
					return nil, errors.New("invalid JSON key")
				}
				if _, exists := object[key]; exists {
					return nil, errors.New("duplicate JSON key")
				}
				child, err := readJSON(decoder, depth+1)
				if err != nil {
					return nil, err
				}
				object[key] = child
			}
			_, err := decoder.Token()
			return object, err
		case '[':
			array := []any{}
			for decoder.More() {
				child, err := readJSON(decoder, depth+1)
				if err != nil {
					return nil, err
				}
				array = append(array, child)
				if len(array) > 10000 {
					return nil, errors.New("JSON array limit")
				}
			}
			_, err := decoder.Token()
			return array, err
		default:
			return nil, errors.New("unexpected JSON delimiter")
		}
	}
	if number, ok := token.(json.Number); ok {
		integer, err := number.Int64()
		if err != nil || integer > 9007199254740991 || integer < -9007199254740991 {
			return nil, errors.New("JSON requires safe integers")
		}
	}
	return token, nil
}

func Canonical(value any) []byte {
	var buffer bytes.Buffer
	writeCanonical(&buffer, value)
	return buffer.Bytes()
}

func writeCanonical(buffer *bytes.Buffer, value any) {
	switch typed := value.(type) {
	case map[string]any:
		keys := make([]string, 0, len(typed))
		for key := range typed {
			keys = append(keys, key)
		}
		sort.Slice(keys, func(first, second int) bool {
			left, right := utf16.Encode([]rune(keys[first])), utf16.Encode([]rune(keys[second]))
			for index := 0; index < len(left) && index < len(right); index++ {
				if left[index] != right[index] {
					return left[index] < right[index]
				}
			}
			return len(left) < len(right)
		})
		buffer.WriteByte('{')
		for index, key := range keys {
			if index > 0 {
				buffer.WriteByte(',')
			}
			writeCanonical(buffer, key)
			buffer.WriteByte(':')
			writeCanonical(buffer, typed[key])
		}
		buffer.WriteByte('}')
	case []any:
		buffer.WriteByte('[')
		for index, child := range typed {
			if index > 0 {
				buffer.WriteByte(',')
			}
			writeCanonical(buffer, child)
		}
		buffer.WriteByte(']')
	case json.Number:
		integer, err := typed.Int64()
		if err != nil {
			panic(err)
		}
		buffer.WriteString(strconv.FormatInt(integer, 10))
	default:
		var encoded bytes.Buffer
		encoder := json.NewEncoder(&encoded)
		encoder.SetEscapeHTML(false)
		if err := encoder.Encode(value); err != nil {
			panic(err)
		}
		text := strings.TrimSuffix(encoded.String(), "\n")
		text = strings.ReplaceAll(strings.ReplaceAll(text, `\u2028`, "\u2028"), `\u2029`, "\u2029")
		buffer.WriteString(text)
	}
}

func ValidateSchema(value any, schema map[string]any) error {
	if names, ok := schema["type"].([]any); ok {
		for _, name := range names {
			branch := map[string]any{}
			for key, child := range schema {
				branch[key] = child
			}
			branch["type"] = name
			if ValidateSchema(value, branch) == nil {
				return nil
			}
		}
		return errors.New("schema type union mismatch")
	}
	for _, keyword := range []string{"allOf", "anyOf", "oneOf"} {
		if branches, ok := schema[keyword].([]any); ok {
			matches := 0
			for _, branch := range branches {
				if ValidateSchema(value, branch.(map[string]any)) == nil {
					matches++
				}
			}
			if (keyword == "allOf" && matches != len(branches)) || (keyword == "anyOf" && matches == 0) || (keyword == "oneOf" && matches != 1) {
				return errors.New("schema branch mismatch")
			}
		}
	}
	if condition, ok := schema["if"].(map[string]any); ok && ValidateSchema(value, condition) == nil {
		if then, ok := schema["then"].(map[string]any); ok {
			if err := ValidateSchema(value, then); err != nil {
				return err
			}
		}
	}
	if expected, exists := schema["const"]; exists && !bytes.Equal(Canonical(value), Canonical(expected)) {
		return errors.New("schema constant mismatch")
	}
	if choices, ok := schema["enum"].([]any); ok {
		found := false
		for _, choice := range choices {
			found = found || bytes.Equal(Canonical(value), Canonical(choice))
		}
		if !found {
			return errors.New("schema enum mismatch")
		}
	}
	typeName, _ := schema["type"].(string)
	if typeName == "" && (schema["properties"] != nil || schema["required"] != nil) {
		typeName = "object"
	}
	switch typeName {
	case "object":
		object, ok := value.(map[string]any)
		if !ok {
			return errors.New("schema expects object")
		}
		properties, _ := schema["properties"].(map[string]any)
		if required, ok := schema["required"].([]any); ok {
			for _, key := range required {
				if _, exists := object[key.(string)]; !exists {
					return fmt.Errorf("required field %s", key)
				}
			}
		}
		for key, child := range object {
			if property, exists := properties[key]; exists {
				if err := ValidateSchema(child, property.(map[string]any)); err != nil {
					return fmt.Errorf("field %s: %w", key, err)
				}
			} else if allow, exists := schema["additionalProperties"].(bool); exists && !allow {
				return fmt.Errorf("unknown field %s", key)
			}
		}
	case "array":
		array, ok := value.([]any)
		if !ok {
			return errors.New("schema expects array")
		}
		if err := bounds(int64(len(array)), schema, "minItems", "maxItems"); err != nil {
			return err
		}
		seen := map[string]bool{}
		for _, child := range array {
			if unique, _ := schema["uniqueItems"].(bool); unique {
				key := string(Canonical(child))
				if seen[key] {
					return errors.New("duplicate array value")
				}
				seen[key] = true
			}
			if item, ok := schema["items"].(map[string]any); ok {
				if err := ValidateSchema(child, item); err != nil {
					return err
				}
			}
		}
	case "integer", "number":
		number, ok := value.(json.Number)
		if !ok {
			return errors.New("schema expects integer")
		}
		integer, err := number.Int64()
		if err != nil {
			return err
		}
		if err := bounds(integer, schema, "minimum", "maximum"); err != nil {
			return err
		}
	case "string":
		text, ok := value.(string)
		if !ok {
			return errors.New("schema expects string")
		}
		if err := bounds(int64(utf8.RuneCountInString(text)), schema, "minLength", "maxLength"); err != nil {
			return err
		}
		if pattern, ok := schema["pattern"].(string); ok {
			matched, err := regexp.MatchString(pattern, text)
			if err != nil || !matched {
				return errors.New("schema string pattern")
			}
		}
		if schema["format"] == "uuid" && !uuidPattern.MatchString(text) {
			return errors.New("schema UUID format")
		}
	case "boolean":
		if _, ok := value.(bool); !ok {
			return errors.New("schema expects boolean")
		}
	case "null":
		if value != nil {
			return errors.New("schema expects null")
		}
	}
	return nil
}

var uuidPattern = regexp.MustCompile(`^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$`)

func bounds(value int64, schema map[string]any, lower, upper string) error {
	if raw, exists := schema[lower]; exists {
		minimum, err := strconv.ParseInt(fmt.Sprint(raw), 10, 64)
		if err != nil || value < minimum {
			return errors.New("schema lower bound")
		}
	}
	if raw, exists := schema[upper]; exists {
		maximum, err := strconv.ParseInt(fmt.Sprint(raw), 10, 64)
		if err != nil || value > maximum {
			return errors.New("schema upper bound")
		}
	}
	return nil
}
